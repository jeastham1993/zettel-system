terraform {
  required_version = ">= 1.7"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }

  # Remote state — create this S3 bucket + DynamoDB table manually before first apply.
  # See docs/deployment-runbook.md for bootstrap instructions.
  backend "s3" {
    bucket         = "zettel-terraform-state"
    key            = "prod/terraform.tfstate"
    region         = "eu-west-1"
    dynamodb_table = "zettel-terraform-locks"
    encrypt        = true
  }
}

provider "aws" {
  region = var.aws_region
}

# Random suffix for globally-unique resource names (Cognito domain, S3 buckets)
resource "random_id" "suffix" {
  byte_length = 4
}

module "ecr" {
  source            = "../../modules/ecr"
  app_name          = var.app_name
  github_repository = var.github_repository
}

module "networking" {
  source     = "../../modules/networking"
  app_name   = var.app_name
  aws_region = var.aws_region
}

module "database" {
  source             = "../../modules/database"
  app_name           = var.app_name
  vpc_id             = module.networking.vpc_id
  subnet_ids         = module.networking.private_subnet_ids
  allowed_sg_ids     = [module.api.lambda_security_group_id, module.workers.lambda_security_group_id]
  db_name            = var.db_name
  db_master_username = var.db_master_username
  db_master_password = var.db_master_password
}

module "secrets" {
  source                = "../../modules/secrets"
  app_name              = var.app_name
  db_connection_string  = "Host=${module.database.cluster_endpoint};Database=${var.db_name};Username=${var.db_master_username};Password=${var.db_master_password};Max Pool Size=10;Connection Idle Lifetime=300"
  embedding_provider    = var.embedding_provider
  embedding_model       = var.embedding_model
  embedding_api_key     = var.embedding_api_key
  capture_sqs_queue_url = var.capture_sqs_queue_url
  cors_allowed_origins  = "https://${module.frontend.cloudfront_domain}"
}

module "frontend" {
  source   = "../../modules/frontend"
  app_name = var.app_name
  suffix   = random_id.suffix.hex
}

module "auth" {
  source                = "../../modules/auth"
  app_name              = var.app_name
  aws_region            = var.aws_region
  suffix                = random_id.suffix.hex
  cloudfront_domain     = module.frontend.cloudfront_domain
  api_gateway_id        = module.api.api_gateway_id
  lambda_integration_id = module.api.lambda_integration_id
}

module "api" {
  source              = "../../modules/api"
  app_name            = var.app_name
  aws_region          = var.aws_region
  vpc_id              = module.networking.vpc_id
  subnet_ids          = module.networking.private_subnet_ids
  ecr_image_uri       = "${module.ecr.backend_repository_url}:${var.image_tag}"
  secrets_arn         = module.secrets.secrets_arn
  db_secret_arn       = module.secrets.db_secret_arn
  cors_allowed_origin = "https://${module.frontend.cloudfront_domain}"
  log_group_arn       = module.monitoring.api_log_group_arn
}

module "workers" {
  source                      = "../../modules/workers"
  app_name                    = var.app_name
  aws_region                  = var.aws_region
  vpc_id                      = module.networking.vpc_id
  subnet_ids                  = module.networking.private_subnet_ids
  ecr_image_uri               = "${module.ecr.backend_repository_url}:${var.image_tag}"
  secrets_arn                 = module.secrets.secrets_arn
  db_secret_arn               = module.secrets.db_secret_arn
  capture_queue_arn           = var.capture_queue_arn
  capture_queue_url           = var.capture_sqs_queue_url
  content_generation_schedule = var.content_generation_schedule
  log_group_arn               = module.monitoring.workers_log_group_arn
}

module "monitoring" {
  source                = "../../modules/monitoring"
  app_name              = var.app_name
  alert_email           = var.alert_email
  api_lambda_name       = module.api.lambda_function_name
  embedding_lambda_name = module.workers.embedding_lambda_name
}

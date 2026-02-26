variable "aws_region" {
  description = "AWS region for all resources"
  type        = string
  default     = "eu-west-1"
}

variable "app_name" {
  description = "Application name used as a prefix for all resource names"
  type        = string
  default     = "zettel"
}

variable "image_tag" {
  description = "Container image tag to deploy (git SHA from CI)"
  type        = string
}

variable "db_name" {
  description = "PostgreSQL database name"
  type        = string
  default     = "zettelweb"
}

variable "db_master_username" {
  description = "Aurora master username"
  type        = string
  default     = "zettel"
  sensitive   = true
}

variable "db_master_password" {
  description = "Aurora master password"
  type        = string
  sensitive   = true
}

variable "embedding_provider" {
  description = "Embedding provider: openai, bedrock, or ollama"
  type        = string
  default     = "bedrock"
}

variable "embedding_model" {
  description = "Embedding model identifier"
  type        = string
  default     = "amazon.titan-embed-text-v2:0"
}

variable "embedding_api_key" {
  description = "API key for embedding provider (empty string for Bedrock which uses IAM)"
  type        = string
  default     = ""
  sensitive   = true
}

variable "capture_sqs_queue_url" {
  description = "URL of the existing SQS capture queue (from ADR-004 CDK stack). Leave empty to disable."
  type        = string
  default     = ""
}

variable "capture_queue_arn" {
  description = "ARN of the existing SQS capture queue. Leave empty to disable."
  type        = string
  default     = ""
}

variable "content_generation_schedule" {
  description = "EventBridge cron expression for content generation (e.g. 'cron(0 9 ? * MON *)')"
  type        = string
  default     = "cron(0 9 ? * MON *)"
}

variable "alert_email" {
  description = "Email address for CloudWatch alarm notifications"
  type        = string
}

variable "github_repository" {
  description = "GitHub repository in owner/repo format (e.g. 'jeastham1993/zettel-system')"
  type        = string
  default     = "jeastham1993/zettel-system"
}

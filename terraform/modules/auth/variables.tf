variable "app_name" {
  type = string
}

variable "aws_region" {
  type = string
}

variable "suffix" {
  description = "Random suffix for unique Cognito domain"
  type        = string
}

variable "cloudfront_domain" {
  description = "CloudFront domain (without https://)"
  type        = string
}

variable "api_gateway_id" {
  type = string
}

variable "lambda_integration_id" {
  type = string
}

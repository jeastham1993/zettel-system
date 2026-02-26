output "cloudfront_domain" {
  description = "CloudFront distribution domain — use this as the app URL"
  value       = "https://${module.frontend.cloudfront_domain}"
}

output "api_gateway_url" {
  description = "API Gateway HTTP API invoke URL"
  value       = module.api.api_gateway_url
}

output "ecr_backend_repository_url" {
  description = "ECR repository URL for CI/CD to push backend images"
  value       = module.ecr.backend_repository_url
}

output "cognito_user_pool_id" {
  description = "Cognito User Pool ID (needed for admin-create-user CLI command)"
  value       = module.auth.user_pool_id
}

output "cognito_client_id" {
  description = "Cognito App Client ID — set as VITE_COGNITO_CLIENT_ID in frontend build"
  value       = module.auth.client_id
}

output "cognito_domain" {
  description = "Cognito Hosted UI base URL — set as VITE_COGNITO_DOMAIN in frontend build"
  value       = module.auth.cognito_hosted_ui_url
}

output "db_cluster_endpoint" {
  description = "Aurora cluster writer endpoint"
  value       = module.database.cluster_endpoint
  sensitive   = true
}

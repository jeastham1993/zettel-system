output "user_pool_id" {
  description = "Used with admin-create-user CLI command to provision the first user"
  value       = aws_cognito_user_pool.main.id
}

output "client_id" {
  description = "Set as VITE_COGNITO_CLIENT_ID in the frontend build"
  value       = aws_cognito_user_pool_client.web.id
}

output "cognito_hosted_ui_url" {
  description = "Set as VITE_COGNITO_DOMAIN in the frontend build"
  value       = "https://${aws_cognito_user_pool_domain.main.domain}.auth.${var.aws_region}.amazoncognito.com"
}

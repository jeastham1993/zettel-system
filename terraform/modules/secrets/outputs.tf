output "secrets_arn" {
  description = "ARN of the application secrets — grant Lambda read access in its IAM policy"
  value       = aws_secretsmanager_secret.app.arn
}

# Alias for clarity in root module — the DB connection string lives in the main secret
output "db_secret_arn" {
  value = aws_secretsmanager_secret.app.arn
}

# Single secret containing all application configuration values.
# Lambda reads this at cold start via the AWS Secrets Manager extension.
resource "aws_secretsmanager_secret" "app" {
  name                    = "${var.app_name}/config"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "app" {
  secret_id = aws_secretsmanager_secret.app.id

  secret_string = jsonencode({
    ConnectionStrings__DefaultConnection = var.db_connection_string
    Embedding__Provider                  = var.embedding_provider
    Embedding__Model                     = var.embedding_model
    Embedding__ApiKey                    = var.embedding_api_key
    Capture__SqsQueueUrl                 = var.capture_sqs_queue_url
    Cors__AllowedOrigins__0              = var.cors_allowed_origins
  })
}

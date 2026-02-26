output "lambda_security_group_id" {
  value = aws_security_group.lambda.id
}

output "embedding_lambda_name" {
  value = aws_lambda_function.embedding_worker.function_name
}

output "migration_result" {
  description = "Result of the most recent migration Lambda invocation"
  value       = aws_lambda_invocation.run_migrations.result
  sensitive   = true
}

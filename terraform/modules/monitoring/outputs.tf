output "api_log_group_arn" {
  value = aws_cloudwatch_log_group.api.arn
}

output "workers_log_group_arn" {
  value = aws_cloudwatch_log_group.workers.arn
}

resource "aws_cloudwatch_log_group" "api" {
  name              = "/aws/lambda/${var.app_name}-api"
  retention_in_days = 30
}

resource "aws_cloudwatch_log_group" "workers" {
  name              = "/aws/lambda/${var.app_name}-workers"
  retention_in_days = 14
}

resource "aws_sns_topic" "alerts" {
  name = "${var.app_name}-alerts"
}

resource "aws_sns_topic_subscription" "email" {
  topic_arn = aws_sns_topic.alerts.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

resource "aws_cloudwatch_metric_alarm" "api_errors" {
  alarm_name          = "${var.app_name}-api-errors"
  alarm_description   = "API Lambda error rate too high"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 5
  treat_missing_data  = "notBreaching"

  dimensions = { FunctionName = var.api_lambda_name }

  alarm_actions = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "embedding_errors" {
  alarm_name          = "${var.app_name}-embedding-errors"
  alarm_description   = "Embedding worker Lambda failing"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 3
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 3
  treat_missing_data  = "notBreaching"

  dimensions = { FunctionName = var.embedding_lambda_name }

  alarm_actions = [aws_sns_topic.alerts.arn]
}

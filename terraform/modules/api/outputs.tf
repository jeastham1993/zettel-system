output "api_gateway_id" {
  value = aws_apigatewayv2_api.main.id
}

output "api_gateway_url" {
  value = aws_apigatewayv2_stage.default.invoke_url
}

output "lambda_integration_id" {
  value = aws_apigatewayv2_integration.lambda.id
}

output "lambda_function_name" {
  value = aws_lambda_function.api.function_name
}

output "lambda_security_group_id" {
  description = "Security group ID — passed to database module to allow Lambda → Aurora"
  value       = aws_security_group.lambda.id
}

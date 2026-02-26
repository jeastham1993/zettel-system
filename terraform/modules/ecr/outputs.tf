output "backend_repository_url" {
  description = "ECR repository URL for the backend image"
  value       = aws_ecr_repository.backend.repository_url
}

output "registry_id" {
  description = "AWS account ID owning the registry"
  value       = aws_ecr_repository.backend.registry_id
}

output "github_actions_role_arn" {
  description = "IAM role ARN for GitHub Actions OIDC authentication — set as AWS_ROLE_ARN in repository secrets"
  value       = aws_iam_role.github_actions.arn
}

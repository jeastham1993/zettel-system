variable "app_name" {
  description = "Application name prefix for ECR repository names"
  type        = string
}

variable "github_repository" {
  description = "GitHub repository in owner/repo format (e.g. 'jeastham1993/zettel-system')"
  type        = string
}

variable "app_name" {
  type = string
}

variable "aws_region" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "subnet_ids" {
  type = list(string)
}

variable "ecr_image_uri" {
  type = string
}

variable "secrets_arn" {
  type = string
}

variable "db_secret_arn" {
  type = string
}

variable "capture_queue_arn" {
  type    = string
  default = ""
}

variable "capture_queue_url" {
  type    = string
  default = ""
}

variable "content_generation_schedule" {
  type = string
}

variable "log_group_arn" {
  type = string
}

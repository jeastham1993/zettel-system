variable "app_name" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "subnet_ids" {
  type = list(string)
}

variable "allowed_sg_ids" {
  description = "Security group IDs permitted to connect on port 5432 (Lambda functions)"
  type        = list(string)
}

variable "db_name" {
  type = string
}

variable "db_master_username" {
  type      = string
  sensitive = true
}

variable "db_master_password" {
  type      = string
  sensitive = true
}

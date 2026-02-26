variable "app_name" {
  type = string
}

variable "db_connection_string" {
  type      = string
  sensitive = true
}

variable "embedding_provider" {
  type = string
}

variable "embedding_model" {
  type = string
}

variable "embedding_api_key" {
  type      = string
  default   = ""
  sensitive = true
}

variable "capture_sqs_queue_url" {
  type    = string
  default = ""
}

variable "cors_allowed_origins" {
  type = string
}

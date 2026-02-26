output "cluster_endpoint" {
  description = "Aurora writer endpoint used in the connection string"
  value       = aws_rds_cluster.main.endpoint
}

output "security_group_id" {
  value = aws_security_group.aurora.id
}

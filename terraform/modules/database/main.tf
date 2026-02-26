resource "aws_db_subnet_group" "main" {
  name       = "${var.app_name}-db-subnet-group"
  subnet_ids = var.subnet_ids
}

# Security group: only accept PostgreSQL connections from Lambda security groups
resource "aws_security_group" "aurora" {
  name        = "${var.app_name}-aurora"
  description = "Allow PostgreSQL access from Lambda functions"
  vpc_id      = var.vpc_id

  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = var.allowed_sg_ids
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_rds_cluster" "main" {
  cluster_identifier = "${var.app_name}-cluster"

  engine         = "aurora-postgresql"
  engine_version = "16.6"
  engine_mode    = "provisioned" # Required for Serverless v2

  database_name   = var.db_name
  master_username = var.db_master_username
  master_password = var.db_master_password

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.aurora.id]

  serverlessv2_scaling_configuration {
    min_capacity = 0.5 # Stays warm — avoids 15-30s resume latency
    max_capacity = 4
    # seconds_until_auto_pause is not set: at min 0.5 ACU, Aurora does not pause.
    # Auto-pause only activates at min 0 ACU (not used here — see ADR-011).
  }

  # Enable pgvector extension via cluster parameter group
  db_cluster_parameter_group_name = aws_rds_cluster_parameter_group.main.name

  storage_encrypted         = true
  skip_final_snapshot       = false
  final_snapshot_identifier = "${var.app_name}-final-snapshot"

  enabled_cloudwatch_logs_exports = ["postgresql"]
}

resource "aws_rds_cluster_instance" "main" {
  cluster_identifier = aws_rds_cluster.main.id
  instance_class     = "db.serverless"
  engine             = aws_rds_cluster.main.engine
  engine_version     = aws_rds_cluster.main.engine_version

  db_subnet_group_name       = aws_db_subnet_group.main.name
  publicly_accessible        = false
  auto_minor_version_upgrade = true
}

resource "aws_rds_cluster_parameter_group" "main" {
  name        = "${var.app_name}-pg16"
  family      = "aurora-postgresql16"
  description = "Aurora PostgreSQL 16 parameter group with pgvector"

  # pgvector does not require shared_preload_libraries — it is installed via CREATE EXTENSION.
  # The MigrationLambda runs the EF Core migration that contains:
  #   migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:vector", ",,")
  # which generates: CREATE EXTENSION IF NOT EXISTS "vector"
}

resource "aws_security_group" "lambda" {
  name        = "${var.app_name}-workers-lambda"
  description = "Worker Lambda egress to Aurora and AWS services"
  vpc_id      = var.vpc_id

  egress {
    from_port   = 5432
    to_port     = 5432
    protocol    = "tcp"
    cidr_blocks = ["10.0.0.0/16"]
  }

  egress {
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_iam_role" "workers" {
  name = "${var.app_name}-workers-lambda"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "vpc_access" {
  role       = aws_iam_role.workers.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

resource "aws_iam_role_policy" "workers_permissions" {
  name = "workers-permissions"
  role = aws_iam_role.workers.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = var.secrets_arn
      },
      {
        Effect = "Allow"
        Action = [
          "bedrock:InvokeModel",
          "bedrock:InvokeModelWithResponseStream"
        ]
        Resource = "*"
      },
      {
        # SQS permissions for the capture queue event source mapping
        Effect = "Allow"
        Action = [
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:GetQueueAttributes",
          "sqs:ChangeMessageVisibility"
        ]
        Resource = var.capture_queue_arn != "" ? [var.capture_queue_arn] : ["arn:aws:sqs:*:*:*"]
      },
      {
        Effect   = "Allow"
        Action   = ["logs:CreateLogGroup", "logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = "${var.log_group_arn}:*"
      }
    ]
  })
}

# ── Embedding Worker Lambda ──────────────────────────────────────────────────
# Triggered every 60 seconds by EventBridge Scheduler.
# Invokes EmbeddingWorkerHandler which calls EmbeddingBackgroundService's
# public ProcessNoteAsync / GetPendingNoteIdsAsync methods.
resource "aws_lambda_function" "embedding_worker" {
  function_name = "${var.app_name}-embedding-worker"
  role          = aws_iam_role.workers.arn
  package_type  = "Image"
  image_uri     = var.ecr_image_uri

  image_config {
    command = ["ZettelWeb::ZettelWeb.Lambda.EmbeddingWorkerHandler::HandleAsync"]
  }

  memory_size = 256
  timeout     = 300 # 5 minutes — handles large batches of pending notes

  vpc_config {
    subnet_ids         = var.subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      ASPNETCORE_ENVIRONMENT = "Production"
      APP_SECRETS_ARN        = var.secrets_arn
      Embedding__Dimensions  = "1024"
    }
  }

  depends_on = [aws_iam_role_policy_attachment.vpc_access]
}

resource "aws_scheduler_schedule" "embedding_worker" {
  name       = "${var.app_name}-embedding-worker"
  group_name = "default"

  flexible_time_window { mode = "OFF" }

  # Run every 60 seconds
  schedule_expression = "rate(1 minute)"

  target {
    arn      = aws_lambda_function.embedding_worker.arn
    role_arn = aws_iam_role.scheduler.arn
    input    = jsonencode({})
  }
}

# ── Capture Queue Worker Lambda ──────────────────────────────────────────────
# Replaces SqsPollingBackgroundService entirely.
# AWS manages the long-poll loop; Lambda is invoked with batches of SQS messages.
# Only created when a capture queue ARN is provided.
resource "aws_lambda_function" "capture_worker" {
  count         = var.capture_queue_arn != "" ? 1 : 0
  function_name = "${var.app_name}-capture-worker"
  role          = aws_iam_role.workers.arn
  package_type  = "Image"
  image_uri     = var.ecr_image_uri

  image_config {
    command = ["ZettelWeb::ZettelWeb.Lambda.CaptureWorkerHandler::HandleAsync"]
  }

  memory_size = 256
  timeout     = 120

  vpc_config {
    subnet_ids         = var.subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      ASPNETCORE_ENVIRONMENT = "Production"
      APP_SECRETS_ARN        = var.secrets_arn
    }
  }

  depends_on = [aws_iam_role_policy_attachment.vpc_access]
}

resource "aws_lambda_event_source_mapping" "capture_queue" {
  count            = var.capture_queue_arn != "" ? 1 : 0
  event_source_arn = var.capture_queue_arn
  function_name    = aws_lambda_function.capture_worker[0].arn
  batch_size       = 10
  enabled          = true
}

# ── Content Generation Scheduler Lambda ─────────────────────────────────────
resource "aws_lambda_function" "content_schedule" {
  function_name = "${var.app_name}-content-schedule"
  role          = aws_iam_role.workers.arn
  package_type  = "Image"
  image_uri     = var.ecr_image_uri

  image_config {
    command = ["ZettelWeb::ZettelWeb.Lambda.ContentScheduleHandler::HandleAsync"]
  }

  memory_size = 256
  timeout     = 900 # 15 minutes — LLM content generation can be slow

  vpc_config {
    subnet_ids         = var.subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      ASPNETCORE_ENVIRONMENT = "Production"
      APP_SECRETS_ARN        = var.secrets_arn
    }
  }

  depends_on = [aws_iam_role_policy_attachment.vpc_access]
}

resource "aws_scheduler_schedule" "content_schedule" {
  name       = "${var.app_name}-content-schedule"
  group_name = "default"

  flexible_time_window { mode = "OFF" }
  schedule_expression = var.content_generation_schedule

  target {
    arn      = aws_lambda_function.content_schedule.arn
    role_arn = aws_iam_role.scheduler.arn
    input    = jsonencode({})
  }
}

# ── Migration Lambda ──────────────────────────────────────────────────────────
# Invoked once during terraform apply to run EF Core migrations.
# Executed BEFORE the API Lambda image is updated to ensure the schema is ready.
resource "aws_lambda_function" "migration" {
  function_name = "${var.app_name}-migration"
  role          = aws_iam_role.workers.arn
  package_type  = "Image"
  image_uri     = var.ecr_image_uri

  image_config {
    command = ["ZettelWeb::ZettelWeb.Lambda.MigrationHandler::HandleAsync"]
  }

  memory_size = 256
  timeout     = 300

  vpc_config {
    subnet_ids         = var.subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      ASPNETCORE_ENVIRONMENT = "Production"
      APP_SECRETS_ARN        = var.secrets_arn
      Embedding__Dimensions  = "1024"
    }
  }

  depends_on = [aws_iam_role_policy_attachment.vpc_access]
}

# Invoke the migration Lambda automatically during terraform apply
resource "aws_lambda_invocation" "run_migrations" {
  function_name = aws_lambda_function.migration.function_name
  input         = jsonencode({ action = "migrate" })

  # Re-invoke whenever the image tag changes (i.e., new deployment)
  lifecycle_scope = "CRUD"

  depends_on = [aws_lambda_function.migration]
}

# ── IAM role for EventBridge Scheduler to invoke Lambda ─────────────────────
resource "aws_iam_role" "scheduler" {
  name = "${var.app_name}-scheduler"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "scheduler.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "scheduler_invoke" {
  name = "invoke-lambdas"
  role = aws_iam_role.scheduler.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = "lambda:InvokeFunction"
      Resource = [
        aws_lambda_function.embedding_worker.arn,
        aws_lambda_function.content_schedule.arn,
      ]
    }]
  })
}

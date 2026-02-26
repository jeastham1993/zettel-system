resource "aws_cognito_user_pool" "main" {
  name = "${var.app_name}-users"

  # No self-registration — single admin-created user only
  admin_create_user_config {
    allow_admin_create_user_only = true
  }

  password_policy {
    minimum_length                   = 12
    require_uppercase                = true
    require_numbers                  = true
    require_symbols                  = false
    temporary_password_validity_days = 7
  }

  # No MFA required for a personal single-user app
  mfa_configuration = "OFF"
}

# Unique subdomain for the Cognito Hosted UI login page
resource "aws_cognito_user_pool_domain" "main" {
  domain       = "${var.app_name}-${var.suffix}"
  user_pool_id = aws_cognito_user_pool.main.id
}

# App client configured for PKCE (no client secret — browser-based SPA)
resource "aws_cognito_user_pool_client" "web" {
  name         = "${var.app_name}-web"
  user_pool_id = aws_cognito_user_pool.main.id

  generate_secret = false # Public client — no secret for PKCE

  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["code"]
  allowed_oauth_scopes                 = ["openid", "email"]
  supported_identity_providers         = ["COGNITO"]

  callback_urls = ["https://${var.cloudfront_domain}/callback"]
  logout_urls   = ["https://${var.cloudfront_domain}/"]

  # Access token: 1 hour. Refresh token: 365 days.
  access_token_validity  = 1
  refresh_token_validity = 365
  id_token_validity      = 1

  token_validity_units {
    access_token  = "hours"
    refresh_token = "days"
    id_token      = "hours"
  }
}

# JWT Authorizer — validates Cognito access tokens before Lambda is invoked.
# API Gateway returns 401 for any request missing a valid Bearer token.
resource "aws_apigatewayv2_authorizer" "cognito" {
  api_id           = var.api_gateway_id
  authorizer_type  = "JWT"
  identity_sources = ["$request.header.Authorization"]
  name             = "cognito-jwt"

  jwt_configuration {
    audience = [aws_cognito_user_pool_client.web.id]
    issuer   = "https://cognito-idp.${var.aws_region}.amazonaws.com/${aws_cognito_user_pool.main.id}"
  }
}

# Apply JWT authorizer to all routes via the $default route
resource "aws_apigatewayv2_route" "default_authenticated" {
  api_id             = var.api_gateway_id
  route_key          = "$default"
  target             = "integrations/${var.lambda_integration_id}"
  authorization_type = "JWT"
  authorizer_id      = aws_apigatewayv2_authorizer.cognito.id
}

# Health check endpoint is public — allows load balancer / uptime monitoring
# without requiring a valid Cognito token
resource "aws_apigatewayv2_route" "health" {
  api_id             = var.api_gateway_id
  route_key          = "GET /health"
  target             = "integrations/${var.lambda_integration_id}"
  authorization_type = "NONE"
}

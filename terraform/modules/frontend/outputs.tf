output "cloudfront_domain" {
  description = "CloudFront domain name (without https://) — used in CORS and Cognito callback"
  value       = aws_cloudfront_distribution.frontend.domain_name
}

output "s3_bucket_name" {
  description = "S3 bucket name — used by deploy-frontend.sh to sync built assets"
  value       = aws_s3_bucket.frontend.bucket
}

output "cloudfront_distribution_id" {
  description = "CloudFront distribution ID — used by deploy-frontend.sh to invalidate cache"
  value       = aws_cloudfront_distribution.frontend.id
}

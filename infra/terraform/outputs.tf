output "api_url" {
  description = "URL publica do Gateway."
  value       = var.certificate_arn == "" ? "http://${aws_lb.public.dns_name}" : "https://${aws_lb.public.dns_name}"
}

output "console_url" {
  value = "https://${aws_cloudfront_distribution.console.domain_name}"
}

output "console_bucket" {
  description = "Bucket onde o CI publica o build do console (aws s3 sync dist/ s3://...)."
  value       = aws_s3_bucket.console.bucket
}

output "ecr_repositories" {
  value = { for name, repo in aws_ecr_repository.service : name => repo.repository_url }
}

output "rabbitmq_console_url" {
  value = aws_mq_broker.rabbitmq.instances[0].console_url
}

output "postgres_endpoint" {
  value = aws_db_instance.postgres.address
}

output "sqlserver_endpoint" {
  value = aws_db_instance.sqlserver.address
}

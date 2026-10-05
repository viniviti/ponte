variable "region" {
  description = "Regiao AWS (sa-east-1 = Sao Paulo)."
  type        = string
  default     = "sa-east-1"
}

variable "environment" {
  type    = string
  default = "prod"
}

variable "vpc_cidr" {
  type    = string
  default = "10.40.0.0/16"
}

variable "image_tag" {
  description = "Tag das imagens no ECR (ex.: SHA do commit publicado pelo CI)."
  type        = string
  default     = "latest"
}

variable "certificate_arn" {
  description = "Certificado ACM para o HTTPS do ALB. Vazio = somente HTTP (nao use em producao)."
  type        = string
  default     = ""
}

variable "postgres_instance_class" {
  type    = string
  default = "db.r6g.large"
}

variable "sqlserver_instance_class" {
  type    = string
  default = "db.m6i.xlarge"
}

variable "sqlserver_engine" {
  description = "sqlserver-se suporta Multi-AZ; sqlserver-ex e mais barato para homologacao."
  type        = string
  default     = "sqlserver-se"
}

variable "mq_instance_type" {
  type    = string
  default = "mq.m5.large"
}

variable "services" {
  description = "Dimensionamento de cada servico no ECS Fargate."
  type = map(object({
    cpu           = number
    memory        = number
    desired_count = number
    min_count     = number
    max_count     = number
  }))
  default = {
    gateway    = { cpu = 512, memory = 1024, desired_count = 2, min_count = 2, max_count = 10 }
    ingestion  = { cpu = 512, memory = 1024, desired_count = 3, min_count = 3, max_count = 20 }
    delivery   = { cpu = 1024, memory = 2048, desired_count = 3, min_count = 3, max_count = 40 }
    management = { cpu = 512, memory = 1024, desired_count = 2, min_count = 2, max_count = 6 }
  }
}

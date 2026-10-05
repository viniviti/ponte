terraform {
  required_version = ">= 1.6"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.70"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Estado remoto (recomendado): descomente e ajuste o bucket.
  # backend "s3" {
  #   bucket         = "ponte-terraform-state"
  #   key            = "ponte/prod.tfstate"
  #   region         = "sa-east-1"
  #   dynamodb_table = "ponte-terraform-lock"
  #   encrypt        = true
  # }
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project     = "ponte"
      Environment = var.environment
      ManagedBy   = "terraform"
    }
  }
}

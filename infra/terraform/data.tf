# ---------------------------------------------------------------------------
# Senhas (geradas e guardadas no Secrets Manager; nunca em variaveis de ambiente cruas)
# ---------------------------------------------------------------------------
resource "random_password" "postgres" {
  length  = 32
  special = false
}

resource "random_password" "sqlserver" {
  length           = 32
  special          = true
  override_special = "!#%^*-_+"
}

resource "random_password" "rabbitmq" {
  length  = 32
  special = false # Amazon MQ nao aceita , : = na senha
}

# ---------------------------------------------------------------------------
# PostgreSQL (Ingestion + Delivery) - Multi-AZ com failover automatico
# ---------------------------------------------------------------------------
resource "aws_db_parameter_group" "postgres" {
  name_prefix = "${local.name}-pg16-"
  family      = "postgres16"

  parameter {
    name         = "max_connections"
    value        = "1000"
    apply_method = "pending-reboot"
  }

  parameter {
    name  = "log_min_duration_statement"
    value = "250" # loga queries acima de 250 ms
  }
}

resource "aws_db_instance" "postgres" {
  identifier_prefix = "${local.name}-pg-"
  engine            = "postgres"
  engine_version    = "16.4"
  instance_class    = var.postgres_instance_class

  allocated_storage     = 100
  max_allocated_storage = 1000
  storage_type          = "gp3"
  storage_encrypted     = true

  db_name  = "ponte"
  username = "ponte"
  password = random_password.postgres.result

  multi_az               = true
  db_subnet_group_name   = module.vpc.database_subnet_group_name
  vpc_security_group_ids = [aws_security_group.data.id]
  parameter_group_name   = aws_db_parameter_group.postgres.name

  backup_retention_period      = 14
  performance_insights_enabled = true
  monitoring_interval          = 0
  deletion_protection          = true
  skip_final_snapshot          = false
  final_snapshot_identifier    = "${local.name}-pg-final"
  auto_minor_version_upgrade   = true
}

# ---------------------------------------------------------------------------
# SQL Server (Management) - Multi-AZ quando a edicao permite
# ---------------------------------------------------------------------------
resource "aws_db_instance" "sqlserver" {
  identifier_prefix = "${local.name}-mssql-"
  engine            = var.sqlserver_engine
  engine_version    = "16.00"
  license_model     = "license-included"
  instance_class    = var.sqlserver_instance_class

  allocated_storage     = 100
  max_allocated_storage = 500
  storage_type          = "gp3"
  storage_encrypted     = true

  username = "ponteadmin"
  password = random_password.sqlserver.result

  multi_az               = var.sqlserver_engine != "sqlserver-ex"
  db_subnet_group_name   = module.vpc.database_subnet_group_name
  vpc_security_group_ids = [aws_security_group.data.id]

  backup_retention_period      = 14
  performance_insights_enabled = var.sqlserver_engine != "sqlserver-ex"
  deletion_protection          = true
  skip_final_snapshot          = false
  final_snapshot_identifier    = "${local.name}-mssql-final"
  auto_minor_version_upgrade   = true
}

# ---------------------------------------------------------------------------
# Amazon MQ for RabbitMQ - cluster de 3 nos em 3 AZs (quorum queues replicadas)
# ---------------------------------------------------------------------------
resource "aws_mq_broker" "rabbitmq" {
  broker_name        = "${local.name}-rabbitmq"
  engine_type        = "RabbitMQ"
  engine_version     = "3.13"
  host_instance_type = var.mq_instance_type
  deployment_mode    = "CLUSTER_MULTI_AZ"

  publicly_accessible        = false
  subnet_ids                 = module.vpc.private_subnets
  security_groups            = [aws_security_group.data.id]
  auto_minor_version_upgrade = true

  user {
    username = "ponte"
    password = random_password.rabbitmq.result
  }

  logs {
    general = true
  }
}

locals {
  amqp_endpoint = aws_mq_broker.rabbitmq.instances[0].endpoints[0]
  amqp_url      = replace(local.amqp_endpoint, "amqps://", "amqps://ponte:${random_password.rabbitmq.result}@")

  connection_strings = {
    ingestion_db  = "Host=${aws_db_instance.postgres.address};Database=ponte_ingestion;Username=ponte;Password=${random_password.postgres.result};Maximum Pool Size=100;SSL Mode=Require;Trust Server Certificate=true"
    delivery_db   = "Host=${aws_db_instance.postgres.address};Database=ponte_delivery;Username=ponte;Password=${random_password.postgres.result};Maximum Pool Size=150;SSL Mode=Require;Trust Server Certificate=true"
    management_db = "Server=${aws_db_instance.sqlserver.address},1433;Database=ponte_management;User Id=ponteadmin;Password=${random_password.sqlserver.result};Encrypt=True;TrustServerCertificate=True"
    rabbitmq      = local.amqp_url
  }
}

# for_each sobre as CHAVES (nao sensiveis); o valor sensivel so entra no secret_string.
resource "aws_secretsmanager_secret" "connection" {
  for_each                = toset(["ingestion_db", "delivery_db", "management_db", "rabbitmq"])
  name_prefix             = "${local.name}/${each.key}-"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "connection" {
  for_each      = aws_secretsmanager_secret.connection
  secret_id     = each.value.id
  secret_string = local.connection_strings[each.key]
}

# ---------------------------------------------------------------------------
# Registro de imagens
# ---------------------------------------------------------------------------
resource "aws_ecr_repository" "service" {
  for_each             = var.services
  name                 = "ponte/${each.key}"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

# ---------------------------------------------------------------------------
# Cluster, logs e descoberta de servicos (DNS interno ponte.internal)
# ---------------------------------------------------------------------------
resource "aws_ecs_cluster" "this" {
  name = local.name

  setting {
    name  = "containerInsights"
    value = "enabled"
  }
}

resource "aws_cloudwatch_log_group" "service" {
  for_each          = var.services
  name              = "/ecs/${local.name}/${each.key}"
  retention_in_days = 30
}

resource "aws_service_discovery_private_dns_namespace" "internal" {
  name = "ponte.internal"
  vpc  = module.vpc.vpc_id
}

resource "aws_service_discovery_service" "service" {
  for_each = var.services
  name     = each.key

  dns_config {
    namespace_id   = aws_service_discovery_private_dns_namespace.internal.id
    routing_policy = "MULTIVALUE"

    dns_records {
      ttl  = 10
      type = "A"
    }
  }

  health_check_custom_config {
    failure_threshold = 1
  }
}

# ---------------------------------------------------------------------------
# IAM
# ---------------------------------------------------------------------------
data "aws_iam_policy_document" "ecs_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "execution" {
  name_prefix        = "${local.name}-exec-"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "read_secrets" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [for secret in aws_secretsmanager_secret.connection : secret.arn]
  }
}

resource "aws_iam_role_policy" "read_secrets" {
  role   = aws_iam_role.execution.id
  policy = data.aws_iam_policy_document.read_secrets.json
}

resource "aws_iam_role" "task" {
  name_prefix        = "${local.name}-task-"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume.json
}

# ---------------------------------------------------------------------------
# Configuracao por servico
# ---------------------------------------------------------------------------
locals {
  internal_url = { for name, _ in var.services : name => "http://${name}.ponte.internal:8080" }

  common_environment = {
    ASPNETCORE_ENVIRONMENT = "Production"
    Database__AutoCreate   = "false" # em producao o schema vem do pipeline (dotnet ef migrations bundle)
  }

  service_environment = {
    gateway = {
      Services__Management                                                     = local.internal_url.management
      Cors__Origins__0                                                         = "https://${aws_cloudfront_distribution.console.domain_name}"
      ReverseProxy__Clusters__ingestion__Destinations__ingestion-1__Address    = local.internal_url.ingestion
      ReverseProxy__Clusters__delivery__Destinations__delivery-1__Address      = local.internal_url.delivery
      ReverseProxy__Clusters__management__Destinations__management-1__Address  = local.internal_url.management
    }
    ingestion  = { RabbitMq__ClientName = "ponte-ingestion" }
    delivery   = { RabbitMq__ClientName = "ponte-delivery", RabbitMq__ConsumerConcurrency = "64", RabbitMq__PrefetchCount = "128" }
    management = { RabbitMq__ClientName = "ponte-management" }
  }

  service_secrets = {
    gateway    = {}
    ingestion  = { ConnectionStrings__IngestionDb = "ingestion_db", RabbitMq__ConnectionString = "rabbitmq" }
    delivery   = { ConnectionStrings__DeliveryDb = "delivery_db", RabbitMq__ConnectionString = "rabbitmq" }
    management = { ConnectionStrings__ManagementDb = "management_db", RabbitMq__ConnectionString = "rabbitmq" }
  }
}

resource "aws_ecs_task_definition" "service" {
  for_each                 = var.services
  family                   = "${local.name}-${each.key}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = each.value.cpu
  memory                   = each.value.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([{
    name         = each.key
    image        = "${aws_ecr_repository.service[each.key].repository_url}:${var.image_tag}"
    essential    = true
    portMappings = [{ containerPort = 8080, protocol = "tcp" }]

    environment = [
      for key, value in merge(local.common_environment, local.service_environment[each.key]) : { name = key, value = value }
    ]

    secrets = [
      for key, secret in local.service_secrets[each.key] : { name = key, valueFrom = aws_secretsmanager_secret.connection[secret].arn }
    ]

    # Sem healthCheck de container: a imagem aspnet nao tem curl/wget. O ALB checa o
    # gateway em /health/ready e o YARP faz health check ativo dos servicos internos.

    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.service[each.key].name
        awslogs-region        = var.region
        awslogs-stream-prefix = each.key
      }
    }
  }])
}

resource "aws_ecs_service" "service" {
  for_each        = var.services
  name            = each.key
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.service[each.key].arn
  desired_count   = each.value.desired_count
  launch_type     = "FARGATE"

  # Espalha as tasks entre as AZs (alta disponibilidade).
  network_configuration {
    subnets          = module.vpc.private_subnets
    security_groups  = [aws_security_group.services.id]
    assign_public_ip = false
  }

  service_registries {
    registry_arn = aws_service_discovery_service.service[each.key].arn
  }

  dynamic "load_balancer" {
    for_each = each.key == "gateway" ? [1] : []
    content {
      target_group_arn = aws_lb_target_group.gateway.arn
      container_name   = "gateway"
      container_port   = 8080
    }
  }

  # Deploy seguro: sobe as novas tasks antes de derrubar as antigas e faz rollback
  # automatico se as novas nao ficarem saudaveis.
  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  lifecycle {
    ignore_changes = [desired_count] # quem manda no numero de tasks e o autoscaling
  }
}

# ---------------------------------------------------------------------------
# Autoscaling
# ---------------------------------------------------------------------------
resource "aws_appautoscaling_target" "service" {
  for_each           = var.services
  service_namespace  = "ecs"
  resource_id        = "service/${aws_ecs_cluster.this.name}/${aws_ecs_service.service[each.key].name}"
  scalable_dimension = "ecs:service:DesiredCount"
  min_capacity       = each.value.min_count
  max_capacity       = each.value.max_count
}

resource "aws_appautoscaling_policy" "cpu" {
  for_each           = var.services
  name               = "${each.key}-cpu"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.service[each.key].service_namespace
  resource_id        = aws_appautoscaling_target.service[each.key].resource_id
  scalable_dimension = aws_appautoscaling_target.service[each.key].scalable_dimension

  target_tracking_scaling_policy_configuration {
    target_value       = 60
    scale_in_cooldown  = 120
    scale_out_cooldown = 30

    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }
  }
}

# O Delivery escala pela fila, nao pela CPU: se o backlog de delivery.jobs cresce
# (pico de eventos ou clientes lentos), sobem mais workers.
resource "aws_appautoscaling_policy" "delivery_backlog" {
  name               = "delivery-queue-backlog"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.service["delivery"].service_namespace
  resource_id        = aws_appautoscaling_target.service["delivery"].resource_id
  scalable_dimension = aws_appautoscaling_target.service["delivery"].scalable_dimension

  target_tracking_scaling_policy_configuration {
    target_value       = 1000
    scale_in_cooldown  = 300
    scale_out_cooldown = 30

    customized_metric_specification {
      metric_name = "MessageCount"
      namespace   = "AWS/AmazonMQ"
      statistic   = "Average"

      dimensions {
        name  = "Broker"
        value = aws_mq_broker.rabbitmq.broker_name
      }

      dimensions {
        name  = "VirtualHost"
        value = "/"
      }

      dimensions {
        name  = "Queue"
        value = "delivery.jobs"
      }
    }
  }
}

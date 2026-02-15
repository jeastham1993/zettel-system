import * as cdk from "aws-cdk-lib";
import * as sqs from "aws-cdk-lib/aws-sqs";
import * as lambda from "aws-cdk-lib/aws-lambda-nodejs";
import * as lambdaRuntime from "aws-cdk-lib/aws-lambda";
import * as apigateway from "aws-cdk-lib/aws-apigatewayv2";
import * as integrations from "aws-cdk-lib/aws-apigatewayv2-integrations";
import * as cloudwatch from "aws-cdk-lib/aws-cloudwatch";
import * as actions from "aws-cdk-lib/aws-cloudwatch-actions";
import * as sns from "aws-cdk-lib/aws-sns";
import * as snsSubscriptions from "aws-cdk-lib/aws-sns-subscriptions";
import * as iam from "aws-cdk-lib/aws-iam";
import { Construct } from "constructs";
import * as path from "path";

export class WebhookIngestionStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const alarmEmail = this.node.tryGetContext("alarmEmail") as
      | string
      | undefined;

    // --- SQS ---
    const dlq = new sqs.Queue(this, "WebhookDLQ", {
      queueName: "zettel-webhook-dlq",
      retentionPeriod: cdk.Duration.days(14),
    });

    const queue = new sqs.Queue(this, "WebhookQueue", {
      queueName: "zettel-webhook-queue",
      retentionPeriod: cdk.Duration.days(14),
      visibilityTimeout: cdk.Duration.seconds(120),
      deadLetterQueue: {
        queue: dlq,
        maxReceiveCount: 3,
      },
    });

    // --- SNS topic for SES inbound email ---
    // SES receipt rule publishes to this topic, which delivers to SQS.
    // Raw message delivery ensures the SQS body is the SES notification
    // JSON directly (no SNS envelope wrapper).
    const sesEmailTopic = new sns.Topic(this, "SesEmailTopic", {
      topicName: "zettel-ses-inbound",
      displayName: "Zettel SES Inbound Email",
    });

    sesEmailTopic.addSubscription(
      new snsSubscriptions.SqsSubscription(queue, {
        rawMessageDelivery: true,
      })
    );

    // Allow SES to publish to this topic
    sesEmailTopic.addToResourcePolicy(
      new iam.PolicyStatement({
        effect: iam.Effect.ALLOW,
        principals: [new iam.ServicePrincipal("ses.amazonaws.com")],
        actions: ["sns:Publish"],
        resources: [sesEmailTopic.topicArn],
      })
    );

    // --- Lambda (for Telegram webhook relay) ---
    const relay = new lambda.NodejsFunction(this, "WebhookRelay", {
      functionName: "zettel-webhook-relay",
      entry: path.join(__dirname, "..", "lambda", "webhook-relay.ts"),
      handler: "handler",
      runtime: lambdaRuntime.Runtime.NODEJS_22_X,
      memorySize: 128,
      timeout: cdk.Duration.seconds(10),
      environment: {
        QUEUE_URL: queue.queueUrl,
      },
      bundling: {
        minify: true,
        sourceMap: false,
      },
    });

    queue.grantSendMessages(relay);

    // --- API Gateway (HTTP API) ---
    // Telegram webhook uses API Gateway -> Lambda -> SQS.
    // Email uses SES -> SNS -> SQS (no Lambda needed).
    const httpApi = new apigateway.HttpApi(this, "WebhookApi", {
      apiName: "zettel-webhook-api",
      description: "Public webhook endpoint for Zettel-Web capture",
    });

    const lambdaIntegration = new integrations.HttpLambdaIntegration(
      "RelayIntegration",
      relay
    );

    httpApi.addRoutes({
      path: "/telegram",
      methods: [apigateway.HttpMethod.POST],
      integration: lambdaIntegration,
    });

    // Throttling at the HTTP API stage level
    const cfnStage = httpApi.defaultStage?.node
      .defaultChild as apigateway.CfnStage;
    cfnStage.defaultRouteSettings = {
      throttlingBurstLimit: 10,
      throttlingRateLimit: 10,
    };

    // --- CloudWatch Alarms + SNS ---
    const alarmTopic = new sns.Topic(this, "AlarmTopic", {
      topicName: "zettel-webhook-alarms",
      displayName: "Zettel Webhook Alarms",
    });

    if (alarmEmail) {
      alarmTopic.addSubscription(
        new snsSubscriptions.EmailSubscription(alarmEmail)
      );
    }

    const alarmAction = new actions.SnsAction(alarmTopic);

    // Alarm: oldest message in queue > 1 hour
    const queueAgeAlarm = new cloudwatch.Alarm(this, "QueueAgeAlarm", {
      alarmName: "zettel-webhook-queue-age",
      alarmDescription:
        "Oldest message in webhook queue is over 1 hour old",
      metric: queue.metricApproximateAgeOfOldestMessage({
        period: cdk.Duration.minutes(5),
        statistic: "Maximum",
      }),
      threshold: 3600,
      evaluationPeriods: 1,
      comparisonOperator:
        cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
    });
    queueAgeAlarm.addAlarmAction(alarmAction);

    // Alarm: DLQ has messages
    const dlqDepthAlarm = new cloudwatch.Alarm(this, "DlqDepthAlarm", {
      alarmName: "zettel-webhook-dlq-depth",
      alarmDescription: "Dead letter queue has messages (poison messages)",
      metric: dlq.metricApproximateNumberOfMessagesVisible({
        period: cdk.Duration.minutes(5),
        statistic: "Maximum",
      }),
      threshold: 0,
      evaluationPeriods: 1,
      comparisonOperator:
        cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
    });
    dlqDepthAlarm.addAlarmAction(alarmAction);

    // Alarm: Lambda errors
    const lambdaErrorAlarm = new cloudwatch.Alarm(
      this,
      "LambdaErrorAlarm",
      {
        alarmName: "zettel-webhook-lambda-errors",
        alarmDescription: "Lambda relay function is producing errors",
        metric: relay.metricErrors({
          period: cdk.Duration.minutes(1),
          statistic: "Sum",
        }),
        threshold: 0,
        evaluationPeriods: 5,
        comparisonOperator:
          cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
        treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
      }
    );
    lambdaErrorAlarm.addAlarmAction(alarmAction);

    // --- Stack Outputs ---
    new cdk.CfnOutput(this, "QueueUrl", {
      value: queue.queueUrl,
      description:
        "SQS queue URL for .NET app Capture:SqsQueueUrl config",
    });

    new cdk.CfnOutput(this, "QueueArn", {
      value: queue.queueArn,
      description: "SQS queue ARN for IAM policy",
    });

    new cdk.CfnOutput(this, "SesEmailTopicArn", {
      value: sesEmailTopic.topicArn,
      description:
        "SNS topic ARN for SES receipt rule (publish to this topic)",
    });

    new cdk.CfnOutput(this, "ApiGatewayUrl", {
      value: httpApi.apiEndpoint,
      description:
        "API Gateway URL for Telegram webhook configuration",
    });

    new cdk.CfnOutput(this, "DlqUrl", {
      value: dlq.queueUrl,
      description: "Dead letter queue URL for manual inspection",
    });

    new cdk.CfnOutput(this, "PollerPolicyJson", {
      value: JSON.stringify({
        Version: "2012-10-17",
        Statement: [
          {
            Effect: "Allow",
            Action: [
              "sqs:ReceiveMessage",
              "sqs:DeleteMessage",
              "sqs:GetQueueAttributes",
            ],
            Resource: queue.queueArn,
          },
        ],
      }),
      description:
        "IAM policy JSON for the .NET SQS poller (attach to IAM user/role)",
    });
  }
}

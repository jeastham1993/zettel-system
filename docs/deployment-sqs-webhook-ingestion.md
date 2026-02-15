# Deploying SQS Webhook Ingestion

Last Updated: 2026-02-14

This guide covers deploying the AWS infrastructure that relays email (SES)
and Telegram webhooks to your private Zettel-Web server via SQS.

**Architecture**:
- **Email**: SES receipt rule -> SNS topic -> SQS -> .NET poller
- **Telegram**: API Gateway -> Lambda -> SQS -> .NET poller

See [ADR-004](adr/ADR-004-sqs-webhook-ingestion.md) for design rationale.

## 1. CDK Deployment (AWS Infrastructure)

### Prerequisites

- AWS CLI configured with credentials (`aws configure`)
- Node.js 18+
- AWS CDK CLI: `npm install -g aws-cdk`
- An AWS account with permissions to create Lambda, SQS, API Gateway,
  CloudWatch, SNS, and IAM resources

### Install Dependencies

```bash
cd infra
npm install
```

### Deploy

Deploy with alarm notifications (recommended):

```bash
cdk deploy --context alarmEmail=you@example.com
```

Or without alarm emails:

```bash
cdk deploy
```

CDK will show the resources to be created and ask for confirmation.

### Stack Outputs

After deployment, note these outputs:

| Output | Use |
|--------|-----|
| `QueueUrl` | Set as `Capture:SqsQueueUrl` in your .NET config |
| `SesEmailTopicArn` | SNS topic ARN for SES receipt rule |
| `ApiGatewayUrl` | Base URL for Telegram webhook |
| `PollerPolicyJson` | IAM policy to attach to the .NET poller's IAM user |
| `DlqUrl` | Dead letter queue URL for inspecting failed messages |

### Create an IAM User for the .NET Poller

The .NET backend needs AWS credentials with only SQS read/delete
permissions. Create a least-privilege IAM user:

```bash
aws iam create-user --user-name zettel-sqs-poller

# Use the PollerPolicyJson output from cdk deploy
aws iam put-user-policy \
  --user-name zettel-sqs-poller \
  --policy-name SqsPollerAccess \
  --policy-document '<PollerPolicyJson output>'

aws iam create-access-key --user-name zettel-sqs-poller
```

Save the `AccessKeyId` and `SecretAccessKey` from the last command.

### Configure SES (Email Capture)

SES delivers inbound email to the SQS queue via an SNS topic. No Lambda
is involved in the email path.

1. **Verify your domain** in SES (SES console -> Verified identities).
   The domain must match the email address you'll receive mail at
   (e.g., `capture@yourdomain.com`).

2. **Create an SES receipt rule set** (SES console -> Email receiving ->
   Rule sets). Create a new rule set if you don't have one, then
   activate it.

3. **Create an SES receipt rule** within the rule set:
   - **Recipients**: the email address to capture from
     (e.g., `capture@yourdomain.com`)
   - **Actions**: Add action -> **Publish to Amazon SNS topic**
   - **SNS topic**: Select or paste the `SesEmailTopicArn` from
     the CDK stack output
   - **Encoding**: UTF-8 (to include email content in the notification)

4. **Add allowed senders** to `Capture:AllowedEmailSenders` in your
   .NET config (only emails from these addresses will create notes).

### Configure Telegram Bot Webhook

1. **Create a bot** via [@BotFather](https://t.me/BotFather) on Telegram
   and save the bot token.

2. **Set the webhook URL** to point at your API Gateway endpoint:

   ```bash
   curl "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/setWebhook\
   ?url=<ApiGatewayUrl>/telegram"
   ```

3. **Find your chat ID**. Send any message to your bot in Telegram,
   then call the `getUpdates` endpoint:

   ```bash
   # Temporarily remove the webhook so getUpdates works
   curl "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/deleteWebhook"

   # Fetch recent messages — look for "chat":{"id": ...}
   curl -s "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getUpdates" \
     | python3 -m json.tool
   ```

   The response contains your chat ID at
   `result[0].message.chat.id`. It is a numeric value like
   `123456789`. For group chats the ID is negative (e.g.
   `-100123456789`).

   Once you have the ID, re-enable the webhook:

   ```bash
   curl "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/setWebhook\
   ?url=<ApiGatewayUrl>/telegram"
   ```

4. **Add the chat ID** to `Capture:AllowedTelegramChatIds` in your
   .NET config. Only messages from allowed chat IDs are accepted.

## 2. .NET Backend Configuration

The SQS poller activates only when `Capture:SqsQueueUrl` is set. Leave it
empty to disable SQS polling entirely.

### Environment Variables for Docker Compose

Add these to the `backend` service in `docker-compose.yml`:

```yaml
backend:
  environment:
    # ... existing vars ...
    Capture__SqsQueueUrl: "https://sqs.<region>.amazonaws.com/<account>/zettel-webhook-queue"
    Capture__SqsRegion: "us-east-1"
    Capture__AllowedEmailSenders__0: "you@example.com"
    Capture__AllowedTelegramChatIds__0: "123456789"
    Capture__TelegramBotToken: "your-bot-token"
    AWS_ACCESS_KEY_ID: "${AWS_ACCESS_KEY_ID}"
    AWS_SECRET_ACCESS_KEY: "${AWS_SECRET_ACCESS_KEY}"
```

### AWS Credentials

The .NET SQS SDK picks up credentials in this order:

1. **Environment variables**: `AWS_ACCESS_KEY_ID` and
   `AWS_SECRET_ACCESS_KEY` (simplest for Docker)
2. **Shared credentials file**: `~/.aws/credentials` (mount into
   container)
3. **IAM instance role**: if running on EC2/ECS (no config needed)

For Docker Compose, environment variables are the easiest approach. Store
them in a `.env` file next to `docker-compose.yml` (already in
`.gitignore`):

```env
AWS_ACCESS_KEY_ID=AKIA...
AWS_SECRET_ACCESS_KEY=...
```

### Config Reference

| Key | Default | Description |
|-----|---------|-------------|
| `Capture:SqsQueueUrl` | (empty) | SQS queue URL. Empty = SQS disabled |
| `Capture:SqsRegion` | (empty) | AWS region. Empty = use default chain |
| `Capture:AllowedEmailSenders` | `[]` | Allowed sender emails |
| `Capture:AllowedTelegramChatIds` | `[]` | Allowed Telegram chat IDs |
| `Capture:TelegramBotToken` | (empty) | Telegram bot token |

### Health Check

When SQS polling is enabled, a health check is registered at `/health`
that reports the poller status. Check it with:

```bash
curl http://localhost/health
```

## 3. Monitoring

The CDK stack creates three CloudWatch alarms (routed to email via SNS):

- **Queue age > 1 hour**: messages are not being consumed (server down?)
- **DLQ depth > 0**: poison messages that failed 3 times
- **Lambda errors**: the relay function is failing

To inspect dead letter queue messages:

```bash
aws sqs receive-message \
  --queue-url <DlqUrl> \
  --max-number-of-messages 10
```

import { SQSClient, SendMessageCommand } from "@aws-sdk/client-sqs";
import type { APIGatewayProxyEventV2, APIGatewayProxyResultV2 } from "aws-lambda";

const sqs = new SQSClient({});
const queueUrl = process.env.QUEUE_URL!;

export async function handler(event: APIGatewayProxyEventV2): Promise<APIGatewayProxyResultV2> {
  const path = event.rawPath ?? "";
  const source = path.includes("/email") ? "email" : "telegram";

  await sqs.send(
    new SendMessageCommand({
      QueueUrl: queueUrl,
      MessageBody: event.body ?? "{}",
      MessageAttributes: {
        source: { DataType: "String", StringValue: source },
      },
    })
  );

  return { statusCode: 200, body: JSON.stringify({ ok: true }) };
}

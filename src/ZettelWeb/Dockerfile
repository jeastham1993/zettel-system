FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ZettelWeb.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Lambda Web Adapter — translates API Gateway HTTP API events into standard HTTP requests.
# The application code is unchanged; only the hosting entrypoint changes.
# See: https://github.com/awslabs/aws-lambda-web-adapter
COPY --from=public.ecr.aws/awsguru/aws-lambda-web-adapter:0.9.0 \
     /lambda-adapter /opt/extensions/lambda-adapter

ENV ASPNETCORE_URLS=http://+:8080
# When AWS_LAMBDA_EXEC_WRAPPER is set, Lambda invokes the adapter before the app.
# The adapter is only active in Lambda; this env var is not set in local Docker Compose.
ENV AWS_LAMBDA_EXEC_WRAPPER=/opt/extensions/lambda-adapter

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ZettelWeb.dll"]

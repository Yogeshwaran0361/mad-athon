#!/usr/bin/env bash
# Renders the ECS task definitions in this folder, runs the migrations task, then rolls the API and web services.
# Used by .github/workflows/cd.yml; runnable by hand with the same environment and AWS credentials.
#
# Required env: DEPLOY_ENV (staging|production), AWS_REGION, AWS_ACCOUNT_ID, ECS_CLUSTER, API_IMAGE, WEB_IMAGE,
#   MIGRATOR_IMAGE, APP_BASE_URL, EMAIL_FROM_ADDRESS, API_UPSTREAM (e.g. api.reworkguard.internal:8080 via ECS
#   Service Connect), SUBNET_IDS (comma separated, private), SECURITY_GROUP_IDS (comma separated), VPC_CIDR (trusted
#   proxy network for X-Forwarded-For, e.g. 10.0.0.0/16).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${RENDER_DIR:-$(mktemp -d)}"

for v in DEPLOY_ENV AWS_REGION AWS_ACCOUNT_ID ECS_CLUSTER API_IMAGE WEB_IMAGE MIGRATOR_IMAGE APP_BASE_URL EMAIL_FROM_ADDRESS API_UPSTREAM SUBNET_IDS SECURITY_GROUP_IDS VPC_CIDR; do
  [[ -n "${!v:-}" ]] || { echo "Missing $v" >&2; exit 1; }
done

# Substitutes only the listed ${VAR} placeholders; anything else is left untouched (and fails the jq check if broken).
export RENDER_VARS="DEPLOY_ENV AWS_REGION AWS_ACCOUNT_ID API_IMAGE WEB_IMAGE MIGRATOR_IMAGE APP_BASE_URL EMAIL_FROM_ADDRESS API_UPSTREAM VPC_CIDR"
render() { perl -pe 'BEGIN { %ok = map { $_ => 1 } split / /, $ENV{RENDER_VARS} } s/\$\{(\w+)\}/$ok{$1} ? $ENV{$1} : $&/ge' "$1"; }
for name in api web migrator; do
  render "$here/$name.task-definition.json" > "$out/$name.json"
  jq -e . "$out/$name.json" >/dev/null
done
echo "Rendered task definitions in $out"

[[ "${RENDER_ONLY:-false}" == "true" ]] && exit 0

register() { aws ecs register-task-definition --cli-input-json "file://$out/$1.json" --query 'taskDefinition.taskDefinitionArn' --output text; }
api_td="$(register api)"
web_td="$(register web)"
migrator_td="$(register migrator)"

echo "Running migrations ($migrator_td)"
network="awsvpcConfiguration={subnets=[${SUBNET_IDS}],securityGroups=[${SECURITY_GROUP_IDS}],assignPublicIp=DISABLED}"
task_arn="$(aws ecs run-task --cluster "$ECS_CLUSTER" --launch-type FARGATE --task-definition "$migrator_td" \
  --network-configuration "$network" --started-by "github-actions" --query 'tasks[0].taskArn' --output text)"
aws ecs wait tasks-stopped --cluster "$ECS_CLUSTER" --tasks "$task_arn"
exit_code="$(aws ecs describe-tasks --cluster "$ECS_CLUSTER" --tasks "$task_arn" --query 'tasks[0].containers[0].exitCode' --output text)"
if [[ "$exit_code" != "0" ]]; then
  echo "Migrations failed (exit $exit_code); see CloudWatch log group /reworkguard/$DEPLOY_ENV/migrator" >&2
  exit 1
fi

echo "Rolling services"
aws ecs update-service --cluster "$ECS_CLUSTER" --service "reworkguard-$DEPLOY_ENV-api" --task-definition "$api_td" >/dev/null
aws ecs update-service --cluster "$ECS_CLUSTER" --service "reworkguard-$DEPLOY_ENV-web" --task-definition "$web_td" >/dev/null
aws ecs wait services-stable --cluster "$ECS_CLUSTER" --services "reworkguard-$DEPLOY_ENV-api" "reworkguard-$DEPLOY_ENV-web"
echo "Deployed $api_td and $web_td"

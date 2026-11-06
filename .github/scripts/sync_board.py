"""Keep the project board's Blocked and Todo columns in sync with issue dependencies.

For every open issue on the board whose Status is empty, Blocked or Todo:
- it goes to Blocked if at least one issue it is "blocked by" is still open,
- otherwise it goes to Todo.
In Progress, In Review and Done are never touched.

Needs the `gh` CLI and a token with the `repo` and `project` scopes in GH_TOKEN.
Settings come from env vars: PROJECT_OWNER (user login), PROJECT_NUMBER, DRY_RUN=1 to only print.
"""
import json
import os
import subprocess
import sys

OWNER = os.environ.get("PROJECT_OWNER", "AymericBretin")
NUMBER = int(os.environ.get("PROJECT_NUMBER", "1"))
DRY_RUN = os.environ.get("DRY_RUN") == "1"
MANAGED = {None, "Blocked", "Todo"}


def gh_json(*args):
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"gh {' '.join(args[:2])} failed: {result.stderr.strip()}")
    return json.loads(result.stdout)


def graphql(query, **variables):
    args = ["api", "graphql", "-f", f"query={query}"]
    for key, value in variables.items():
        args += ["-F" if isinstance(value, int) else "-f", f"{key}={value}"]
    return gh_json(*args)["data"]


def load_project():
    query = """
    query($owner: String!, $number: Int!, $cursor: String) {
      user(login: $owner) {
        projectV2(number: $number) {
          id
          field(name: "Status") {
            ... on ProjectV2SingleSelectField { id options { id name } }
          }
          items(first: 100, after: $cursor) {
            pageInfo { hasNextPage endCursor }
            nodes {
              id
              fieldValueByName(name: "Status") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
              content { ... on Issue { number state repository { nameWithOwner } } }
            }
          }
        }
      }
    }"""
    items, cursor, project = [], "", None
    while True:
        variables = {"owner": OWNER, "number": NUMBER}
        if cursor:
            variables["cursor"] = cursor
        project = graphql(query, **variables)["user"]["projectV2"]
        items += project["items"]["nodes"]
        page = project["items"]["pageInfo"]
        if not page["hasNextPage"]:
            break
        cursor = page["endCursor"]
    return project, items


def has_open_blocker(repo, number):
    blockers = gh_json("api", f"repos/{repo}/issues/{number}/dependencies/blocked_by", "--paginate")
    return any(b["state"] == "open" for b in blockers)


def main():
    project, items = load_project()
    field = project["field"]
    options = {o["name"]: o["id"] for o in field["options"]}
    for name in ("Blocked", "Todo"):
        if name not in options:
            sys.exit(f'The Status field has no "{name}" option.')

    moved = 0
    for item in items:
        issue = item["content"]
        if not issue or "number" not in issue or issue["state"] != "OPEN":
            continue
        status = (item["fieldValueByName"] or {}).get("name")
        if status not in MANAGED:
            continue
        target = "Blocked" if has_open_blocker(issue["repository"]["nameWithOwner"], issue["number"]) else "Todo"
        if target == status:
            continue
        print(f"#{issue['number']}: {status or 'no status'} -> {target}")
        moved += 1
        if not DRY_RUN:
            graphql(
                """mutation($project: ID!, $item: ID!, $field: ID!, $option: String!) {
                  updateProjectV2ItemFieldValue(input: {projectId: $project, itemId: $item, fieldId: $field,
                    value: {singleSelectOptionId: $option}}) { projectV2Item { id } }
                }""",
                project=project["id"], item=item["id"], field=field["id"], option=options[target],
            )
    print(f"{moved} card(s) {'would move' if DRY_RUN else 'moved'}.")


if __name__ == "__main__":
    main()

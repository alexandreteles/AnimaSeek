// This repo keeps a single canonical skill copy under .agents/skills (see
// AGENTS.md); .claude/skills and .github/skills are symlinks to it. Instead of
// the provider build rewriting this declaration per copy, the provider is
// detected at runtime from the invocation path, falling back to harness
// environment variables. Keep this file dynamic when re-vendoring the skill.
const invoked = (process.argv[1] ?? "").replace(/\\/g, "/");
const invokedUnder = (dir) =>
  invoked.includes(`/${dir}/`) || invoked.startsWith(`${dir}/`);

let provider = "agents";
if (invokedUnder(".claude")) provider = "claude-code";
else if (invokedUnder(".github")) provider = "github";
else if (process.env.CLAUDECODE === "1" || process.env.CLAUDE_PROJECT_DIR)
  provider = "claude-code";

export const IMPECCABLE_PROVIDER_ID = provider;
export const IMPECCABLE_COMMAND_PREFIX = provider === "agents" ? "$" : "/";
export const IMPECCABLE_COMMAND = `${IMPECCABLE_COMMAND_PREFIX}impeccable`;

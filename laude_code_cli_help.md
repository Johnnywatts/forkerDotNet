> /help 

 Claude Code v1.0.127

 Always review Claude's responses, especially when running code. Claude has read access to files in the current directory and can run commands and edit files with your
 permission.

 Usage Modes:
 • REPL: claude (interactive session)
 • Non-interactive: claude -p "question"

 Run claude -h for all command line options

 Common Tasks:
 • Ask questions about your codebase > How does foo.py work?
 • Edit files > Update bar.ts to...
 • Fix errors > cargo build
 • Run commands > /help
 • Run bash commands > !ls

 Interactive Mode Commands:
  /add-dir - Add a new working directory
  /agents - Manage agent configurations
  /bashes - List and manage background tasks
  /clear - Clear conversation history and free up context
  /compact - Clear conversation history but keep a summary in context. Optional: /compact [instructions for summarization]
  /config - Open config panel
  /context - Visualize current context usage as a colored grid
  /cost - Show the total cost and duration of the current session
  /doctor - Diagnose and verify your Claude Code installation and settings
  /exit - Exit the REPL
  /export - Export the current conversation to a file or clipboard
  /feedback - Submit feedback about Claude Code
  /help - Show help and available commands
  /hooks - Manage hook configurations for tool events
  /ide - Manage IDE integrations and show status
  /init - Initialize a new CLAUDE.md file with codebase documentation
  /install-github-app - Set up Claude GitHub Actions for a repository
  /login - Sign in with your Anthropic account
  /memory - Edit Claude memory files
  /model - Set the AI model for Claude Code
  /output-style - Set the output style directly or from a selection menu
  /output-style:new - Create a custom output style
  /permissions - Manage allow & deny tool permission rules
  /pr-comments - Get comments from a GitHub pull request
  /privacy-settings - View and update your privacy settings
  /release-notes - View release notes
  /resume - Resume a conversation
  /review - Review a pull request
  /security-review - Complete a security review of the pending changes on the current branch
  /status - Show Claude Code status including version, model, account, API connectivity, and tool statuses
  /statusline - Set up Claude Code's status line UI
  /terminal-setup - Install Shift+Enter key binding for newlines
  /todos - List current todo items
  /upgrade - Upgrade to Max for higher rate limits and more Opus
  /vim - Toggle between Vim and Normal editing modes

 Learn more at: https://docs.claude.com/s/claude-code
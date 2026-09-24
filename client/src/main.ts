import "./style.css";

interface AgentStatus {
  ready: boolean;
  model: string;
  message: string;
}

interface ChatResponse {
  reply: string;
  sessionId: string;
}

const messageInput = document.querySelector<HTMLTextAreaElement>("#message-input")!;
const sendButton = document.querySelector<HTMLButtonElement>("#send-button")!;
const chatForm = document.querySelector<HTMLFormElement>("#chat-form")!;
const messages = document.querySelector<HTMLDivElement>("#messages")!;
const statusDot = document.querySelector<HTMLSpanElement>("#status-dot")!;
const statusLabel = document.querySelector<HTMLSpanElement>("#status-label")!;
const setupNote = document.querySelector<HTMLDivElement>("#setup-note")!;
const characterCount = document.querySelector<HTMLSpanElement>("#character-count")!;
const newChatButton = document.querySelector<HTMLButtonElement>("#new-chat")!;

let sessionId = getSessionId();
let isSending = false;

chatForm.addEventListener("submit", (event) => {
  event.preventDefault();
  void sendMessage(messageInput.value);
});

messageInput.addEventListener("input", () => {
  characterCount.textContent = `${messageInput.value.length} / 2000`;
  sendButton.disabled = isSending || messageInput.value.trim().length === 0;
  resizeInput();
});

messageInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    if (!sendButton.disabled) {
      void sendMessage(messageInput.value);
    }
  }
});

document.querySelectorAll<HTMLButtonElement>("[data-prompt]").forEach((button) => {
  button.addEventListener("click", () => {
    const prompt = button.dataset.prompt;
    if (prompt) {
      void sendMessage(prompt);
    }
  });
});

newChatButton.addEventListener("click", startNewChat);
void loadStatus();

async function loadStatus(): Promise<void> {
  try {
    const response = await fetch("/api/status");
    if (!response.ok) {
      throw new Error("The agent service is unavailable.");
    }

    const status = (await response.json()) as AgentStatus;
    statusDot.classList.toggle("status-ready", status.ready);
    statusDot.classList.toggle("status-offline", !status.ready);
    statusLabel.textContent = status.ready ? `Online · ${status.model}` : "Setup needed";
    setupNote.hidden = status.ready;
    setupNote.textContent = status.ready
      ? ""
      : `${status.message} See README.md for setup instructions.`;
  } catch {
    statusDot.classList.remove("status-ready");
    statusDot.classList.add("status-offline");
    statusLabel.textContent = "Service unavailable";
    setupNote.hidden = false;
    setupNote.textContent = "Start the .NET app to connect to the assistant.";
  }
}

async function sendMessage(rawMessage: string): Promise<void> {
  const message = rawMessage.trim();
  if (!message || isSending) {
    return;
  }

  appendMessage("user", message);
  messageInput.value = "";
  characterCount.textContent = "0 / 2000";
  resizeInput();
  setSending(true);
  const pendingMessage = appendPendingMessage();

  try {
    const response = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ sessionId, message }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    const result = (await response.json()) as ChatResponse;
    sessionId = result.sessionId;
    window.sessionStorage.setItem("subway-demo-session", sessionId);
    pendingMessage.remove();
    appendMessage("assistant", result.reply);
  } catch (error) {
    pendingMessage.remove();
    const messageText = error instanceof Error
      ? error.message
      : "Something went wrong. Please try again.";
    appendMessage("assistant", messageText, true);
    void loadStatus();
  } finally {
    setSending(false);
    messageInput.focus();
  }
}

function appendMessage(role: "user" | "assistant", text: string, isError = false): void {
  const row = document.createElement("div");
  row.className = `message-row ${role === "user" ? "user-row" : "assistant-row"}`;

  const content = document.createElement("div");
  content.className = "message-content";

  const author = document.createElement("span");
  author.className = "message-author";
  author.textContent = role === "user" ? "You" : "Store Assistant";

  const bubble = document.createElement("div");
  bubble.className = `message-bubble ${role === "user" ? "user-bubble" : "assistant-bubble"}${isError ? " error-bubble" : ""}`;
  bubble.textContent = text;

  const time = document.createElement("span");
  time.className = "message-time";
  time.textContent = new Intl.DateTimeFormat(undefined, {
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date());

  if (role === "assistant") {
    const avatar = document.createElement("div");
    avatar.className = "message-avatar";
    avatar.setAttribute("aria-hidden", "true");
    avatar.textContent = "S";
    row.append(avatar);
  }

  content.append(author, bubble, time);
  row.append(content);
  messages.append(row);
  scrollToLatest();
}

function appendPendingMessage(): HTMLDivElement {
  const row = document.createElement("div");
  row.className = "message-row assistant-row";
  row.setAttribute("role", "status");
  row.setAttribute("aria-label", "Assistant is thinking");

  const avatar = document.createElement("div");
  avatar.className = "message-avatar";
  avatar.setAttribute("aria-hidden", "true");
  avatar.textContent = "S";

  const bubble = document.createElement("div");
  bubble.className = "message-bubble assistant-bubble typing-bubble";
  bubble.innerHTML = '<span></span><span></span><span></span>';

  row.append(avatar, bubble);
  messages.append(row);
  scrollToLatest();
  return row;
}

async function readApiError(response: Response): Promise<string> {
  const body: unknown = await response.json().catch(() => null);
  if (typeof body === "object" && body !== null) {
    const errorBody = body as { detail?: unknown; error?: unknown; title?: unknown };
    if (typeof errorBody.detail === "string") {
      return errorBody.detail;
    }
    if (typeof errorBody.error === "string") {
      return errorBody.error;
    }
    if (typeof errorBody.title === "string") {
      return errorBody.title;
    }
  }
  return `The request failed (${response.status}). Please try again.`;
}

function setSending(sending: boolean): void {
  isSending = sending;
  messageInput.disabled = sending;
  sendButton.disabled = sending || messageInput.value.trim().length === 0;
  document.querySelectorAll<HTMLButtonElement>("[data-prompt], #new-chat")
    .forEach((button) => {
      button.disabled = sending;
    });
}

function startNewChat(): void {
  sessionId = crypto.randomUUID();
  window.sessionStorage.setItem("subway-demo-session", sessionId);
  messages.replaceChildren();
  appendMessage(
    "assistant",
    "Hey there! 👋 I can help you explore our demo sandwich menu, estimate an order, or find something delicious. What sounds good?",
  );
  messageInput.focus();
}

function getSessionId(): string {
  const existing = window.sessionStorage.getItem("subway-demo-session");
  if (existing && /^[\da-f-]{36}$/i.test(existing)) {
    return existing;
  }

  const created = crypto.randomUUID();
  window.sessionStorage.setItem("subway-demo-session", created);
  return created;
}

function resizeInput(): void {
  messageInput.style.height = "auto";
  messageInput.style.height = `${Math.min(messageInput.scrollHeight, 144)}px`;
}

function scrollToLatest(): void {
  messages.scrollTo({ top: messages.scrollHeight, behavior: "smooth" });
}

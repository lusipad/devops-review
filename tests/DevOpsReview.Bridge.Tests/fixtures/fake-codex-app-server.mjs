import readline from "node:readline";

const lines = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const pendingTurns = new Map();

function send(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

lines.on("line", (line) => {
  const message = JSON.parse(line);
  switch (message.method) {
    case "initialize":
      send({ id: message.id, result: { userAgent: "fake-codex" } });
      break;
    case "initialized":
      break;
    case "thread/start":
      send({ id: message.id, result: { thread: { id: "thread-fake" } } });
      break;
    case "thread/resume":
      send({ id: message.id, result: { thread: { id: message.params.threadId } } });
      break;
    case "turn/start": {
      const turnId = `turn-${message.id}`;
      const threadId = message.params.threadId;
      send({ id: message.id, result: { turn: { id: turnId, items: [], status: "inProgress" } } });
      send({ method: "turn/started", params: { threadId, turn: { id: turnId, items: [], status: "inProgress" } } });
      const text = message.params.input?.[0]?.text || "";
      if (text.includes("WAIT_FOR_INTERRUPT")) {
        pendingTurns.set(turnId, threadId);
        break;
      }
      send({
        method: "item/started",
        params: {
          threadId,
          turnId,
          item: { type: "agentMessage", id: "item-commentary", text: "", phase: "commentary" },
        },
      });
      send({
        method: "item/agentMessage/delta",
        params: { threadId, turnId, itemId: "item-commentary", delta: "不应进入最终答案" },
      });
      send({
        method: "item/started",
        params: {
          threadId,
          turnId,
          item: { type: "agentMessage", id: "item-final", text: "", phase: "final_answer" },
        },
      });
      send({
        method: "item/agentMessage/delta",
        params: { threadId, turnId, itemId: "item-final", delta: "来自伪 App Server 的回答" },
      });
      send({
        method: "turn/completed",
        params: { threadId, turn: { id: turnId, items: [], status: "completed" } },
      });
      break;
    }
    case "turn/interrupt": {
      const { threadId, turnId } = message.params;
      pendingTurns.delete(turnId);
      send({ id: message.id, result: {} });
      send({
        method: "turn/completed",
        params: { threadId, turn: { id: turnId, items: [], status: "interrupted" } },
      });
      break;
    }
    default:
      send({ id: message.id, error: { code: -32601, message: `Unsupported method: ${message.method}` } });
      break;
  }
});

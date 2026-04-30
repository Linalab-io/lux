export interface WritableStreamLike {
  write(chunk: string): unknown;
}

export interface LogStreams {
  stderr: WritableStreamLike;
  stdout?: WritableStreamLike;
}

const defaultLogStreams: LogStreams = {
  stderr: process.stderr,
};

export function logToStderr(message: string, streams: LogStreams = defaultLogStreams): void {
  streams.stderr.write(`[unity-ai-bridge-mcp] ${message}\n`);
}

export function logStartupError(error: unknown, streams: LogStreams = defaultLogStreams): void {
  const detail = error instanceof Error ? error.stack ?? error.message : String(error);
  logToStderr(`Startup failed: ${detail}`, streams);
}

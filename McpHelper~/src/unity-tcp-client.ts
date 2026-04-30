import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

export const UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME = 'unity_selected_file_context';
export const UNITY_SELECTED_FILE_CONTEXT_COMMAND = 'get_selected_file_context';
export const LUX_CONTEXT_TOOL_NAME = 'lux_context';
export const LUX_EXECUTE_SHELL_TOOL_NAME = 'lux_execute_shell';
export const LUX_EXECUTE_GIT_TOOL_NAME = 'lux_execute_git';
export const LUX_CONTEXT_COMMAND = 'get_lux_context';
export const LUX_EXECUTE_SHELL_COMMAND = 'execute_lux_shell';
export const LUX_EXECUTE_GIT_COMMAND = 'execute_lux_git';

const DISCOVERY_FILE_RELATIVE_PATH = path.join('Library', 'UnityAiBridge', 'server.json');
const TCP_RESPONSE_TIMEOUT_MS = 5_000;

interface UnityBridgeDiscovery {
  host: string;
  port: number;
  token: string;
  protocolVersion?: string;
  projectPath?: string;
  pid?: number;
  startedAtUtc?: string;
}

interface UnityBridgeResponse {
  ok?: boolean;
  errorCode?: string;
  errorMessage?: string;
  payload?: {
    selectedFileContext?: unknown;
    luxContext?: unknown;
    luxAutomationResult?: unknown;
  } | null;
}

export interface LuxAutomationRequest {
  commandText?: string;
  gitArguments?: string;
  workingDirectory?: string;
  actor?: string;
  approvalGranted?: boolean;
}

export class UnityTcpContextError extends Error {
  constructor(
    message: string,
    readonly code: string,
    readonly data?: unknown,
  ) {
    super(message);
    this.name = 'UnityTcpContextError';
  }
}

export async function getSelectedFileContextFromUnity(projectRoot: string): Promise<unknown> {
  const discovery = await readDiscovery(projectRoot);
  const response = await sendUnityCommand(discovery, UNITY_SELECTED_FILE_CONTEXT_COMMAND, {});

  if (!response.ok) {
    const unityCode = response.errorCode ?? 'unity_tcp_error';
    const unityMessage = response.errorMessage ?? 'Unity TCP request failed.';
    throw new UnityTcpContextError(`Unity TCP rejected ${UNITY_SELECTED_FILE_CONTEXT_COMMAND}: ${unityMessage}`, unityCode, {
      errorCode: response.errorCode,
      errorMessage: response.errorMessage,
    });
  }

  if (!response.payload || !Object.hasOwn(response.payload, 'selectedFileContext')) {
    throw new UnityTcpContextError('Unity TCP response did not include selectedFileContext payload.', 'invalid_response');
  }

  return response.payload.selectedFileContext;
}

export async function getLuxContextFromUnity(projectRoot: string): Promise<unknown> {
  const discovery = await readDiscovery(projectRoot);
  const response = await sendUnityCommand(discovery, LUX_CONTEXT_COMMAND, {});

  if (!response.ok) {
    throwUnityResponseError(response, LUX_CONTEXT_COMMAND);
  }

  if (!response.payload || !Object.hasOwn(response.payload, 'luxContext')) {
    throw new UnityTcpContextError('Unity TCP response did not include luxContext payload.', 'invalid_response');
  }

  return response.payload.luxContext;
}

export async function executeLuxShellInUnity(projectRoot: string, parameters: LuxAutomationRequest): Promise<unknown> {
  return executeLuxAutomationCommand(projectRoot, LUX_EXECUTE_SHELL_COMMAND, parameters);
}

export async function executeLuxGitInUnity(projectRoot: string, parameters: LuxAutomationRequest): Promise<unknown> {
  return executeLuxAutomationCommand(projectRoot, LUX_EXECUTE_GIT_COMMAND, parameters);
}

async function executeLuxAutomationCommand(projectRoot: string, command: string, parameters: LuxAutomationRequest): Promise<unknown> {
  const discovery = await readDiscovery(projectRoot);
  const response = await sendUnityCommand(discovery, command, parameters);

  if (!response.ok) {
    throwUnityResponseError(response, command);
  }

  if (!response.payload || !Object.hasOwn(response.payload, 'luxAutomationResult')) {
    throw new UnityTcpContextError('Unity TCP response did not include luxAutomationResult payload.', 'invalid_response');
  }

  return response.payload.luxAutomationResult;
}

function throwUnityResponseError(response: UnityBridgeResponse, command: string): never {
  const unityCode = response.errorCode ?? 'unity_tcp_error';
  const unityMessage = response.errorMessage ?? 'Unity TCP request failed.';
  throw new UnityTcpContextError(`Unity TCP rejected ${command}: ${unityMessage}`, unityCode, {
    errorCode: response.errorCode,
    errorMessage: response.errorMessage,
  });
}

async function readDiscovery(projectRoot: string): Promise<UnityBridgeDiscovery> {
  const discoveryPath = path.join(projectRoot, DISCOVERY_FILE_RELATIVE_PATH);
  let rawDiscovery: string;

  try {
    rawDiscovery = await fs.readFile(discoveryPath, 'utf8');
  } catch (error) {
    if (isNodeError(error) && error.code === 'ENOENT') {
      throw new UnityTcpContextError(
        `Unity AI Bridge discovery file not found at ${discoveryPath}. Start the Unity TCP server first.`,
        'missing_discovery_file',
        { discoveryPath },
      );
    }

    throw error;
  }

  let parsedDiscovery: unknown;
  try {
    parsedDiscovery = JSON.parse(rawDiscovery);
  } catch (error) {
    throw new UnityTcpContextError(`Unity AI Bridge discovery file is not valid JSON at ${discoveryPath}.`, 'invalid_discovery_file', {
      discoveryPath,
      cause: error instanceof Error ? error.message : String(error),
    });
  }

  if (!isValidDiscovery(parsedDiscovery)) {
    throw new UnityTcpContextError(`Unity AI Bridge discovery file is missing host, port, or token at ${discoveryPath}.`, 'invalid_discovery_file', {
      discoveryPath,
    });
  }

  return parsedDiscovery;
}

async function sendUnityCommand(discovery: UnityBridgeDiscovery, command: string, parameters: LuxAutomationRequest | Record<string, never>): Promise<UnityBridgeResponse> {
  const requestLine = `${JSON.stringify({
    schemaVersion: 1,
    requestId: randomUUID(),
    command,
    token: discovery.token,
    params: parameters,
  })}\n`;

  const responseLine = await sendTcpLine(discovery.host, discovery.port, requestLine);
  let parsedResponse: unknown;

  try {
    parsedResponse = JSON.parse(responseLine);
  } catch (error) {
    throw new UnityTcpContextError('Unity TCP response was not valid JSON.', 'invalid_response', {
      cause: error instanceof Error ? error.message : String(error),
    });
  }

  if (!isObject(parsedResponse)) {
    throw new UnityTcpContextError('Unity TCP response was not a JSON object.', 'invalid_response');
  }

  return parsedResponse as UnityBridgeResponse;
}

function sendTcpLine(host: string, port: number, requestLine: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host, port });
    let buffer = '';
    let settled = false;

    const finish = (error: Error | undefined, responseLine?: string): void => {
      if (settled) {
        return;
      }

      settled = true;
      socket.removeAllListeners();
      socket.destroy();

      if (error) {
        reject(error);
        return;
      }

      resolve(responseLine ?? '');
    };

    socket.setEncoding('utf8');
    socket.setTimeout(TCP_RESPONSE_TIMEOUT_MS);

    socket.on('connect', () => {
      socket.write(requestLine);
    });

    socket.on('data', (chunk) => {
      buffer += chunk;
      const newlineIndex = buffer.indexOf('\n');
      if (newlineIndex >= 0) {
        finish(undefined, buffer.slice(0, newlineIndex));
      }
    });

    socket.on('timeout', () => {
      finish(new UnityTcpContextError(`Unity TCP request timed out after ${TCP_RESPONSE_TIMEOUT_MS}ms.`, 'tcp_timeout', { host, port }));
    });

    socket.on('error', (error) => {
      if (isNodeError(error) && error.code === 'ECONNREFUSED') {
        finish(
          new UnityTcpContextError(`Unity TCP connection refused at ${host}:${port}. Start or restart the Unity TCP server.`, 'connection_refused', {
            host,
            port,
          }),
        );
        return;
      }

      finish(new UnityTcpContextError(`Unity TCP connection failed at ${host}:${port}: ${error.message}`, 'tcp_connection_failed', { host, port }));
    });

    socket.on('end', () => {
      finish(new UnityTcpContextError('Unity TCP connection closed before a response line was received.', 'connection_closed'));
    });
  });
}

function isValidDiscovery(value: unknown): value is UnityBridgeDiscovery {
  if (!isObject(value)) {
    return false;
  }

  return (
    typeof value.host === 'string' &&
    value.host.length > 0 &&
    typeof value.port === 'number' &&
    Number.isInteger(value.port) &&
    value.port > 0 &&
    value.port <= 65_535 &&
    typeof value.token === 'string' &&
    value.token.length > 0
  );
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isNodeError(error: unknown): error is NodeJS.ErrnoException {
  return error instanceof Error && 'code' in error;
}

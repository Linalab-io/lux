import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ErrorCode, ListToolsRequestSchema, McpError } from '@modelcontextprotocol/sdk/types.js';
import { logToStderr } from './logger.js';
import { findUnityProjectRoot } from './project-root.js';
import {
  captureLuxScreenshotInUnity,
  clearLuxConsoleInUnity,
  controlLuxPlayModeInUnity,
  executeLuxDynamicCodeInUnity,
  executeLuxGitInUnity,
  executeLuxShellInUnity,
  findLuxGameObjectsInUnity,
  focusLuxWindowInUnity,
  getLuxContextFromUnity,
  getLuxConsoleLogsFromUnity,
  getLuxHierarchyFromUnity,
  getSelectedFileContextFromUnity,
  LUX_CLEAR_CONSOLE_TOOL_NAME,
  LUX_CONTEXT_TOOL_NAME,
  LUX_CONSOLE_LOGS_TOOL_NAME,
  LUX_DYNAMIC_CODE_TOOL_NAME,
  LUX_EXECUTE_GIT_TOOL_NAME,
  LUX_EXECUTE_SHELL_TOOL_NAME,
  LUX_FIND_GAME_OBJECTS_TOOL_NAME,
  LUX_FOCUS_WINDOW_TOOL_NAME,
  LUX_HIERARCHY_TOOL_NAME,
  LUX_KEYBOARD_TOOL_NAME,
  LUX_MOUSE_INPUT_TOOL_NAME,
  LUX_MOUSE_UI_TOOL_NAME,
  LUX_PLAY_MODE_TOOL_NAME,
  LUX_RECORD_INPUT_TOOL_NAME,
  LUX_REPLAY_INPUT_TOOL_NAME,
  LUX_SCREENSHOT_TOOL_NAME,
  LUX_VERSION_TOOL_NAME,
  type LuxAutomationRequest,
  type LuxToolParameters,
  recordLuxInputInUnity,
  replayLuxInputInUnity,
  simulateLuxKeyboardInUnity,
  simulateLuxMouseInputInUnity,
  simulateLuxMouseUiInUnity,
  UnityTcpContextError,
  UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME,
} from './unity-tcp-client.js';

const SERVER_NAME = 'linalab-unity-ai-bridge';
const SERVER_VERSION = '0.1.0';

interface McpToolDefinition {
  name: string;
  title: string;
  description: string;
  inputSchema: Record<string, unknown>;
  annotations: {
    readOnlyHint: boolean;
    destructiveHint: boolean;
    idempotentHint: boolean;
    openWorldHint: boolean;
  };
}

const EMPTY_INPUT_SCHEMA: Record<string, unknown> = {
  type: 'object',
  properties: {},
  required: [],
  additionalProperties: false,
};

const LUX_TOOL_DEFINITIONS: McpToolDefinition[] = [
  {
    name: LUX_CONSOLE_LOGS_TOOL_NAME,
    title: 'Get Lux Console Logs',
    description: 'Returns Unity console log entries with optional filtering and stack traces.',
    inputSchema: toolInputSchema({
      logType: { type: 'string' },
      maxCount: { type: 'number' },
      searchText: { type: 'string' },
      includeStackTrace: { type: 'boolean' },
      useRegex: { type: 'boolean' },
      searchInStackTrace: { type: 'boolean' },
    }),
    annotations: readOnlyAnnotations(false),
  },
  {
    name: LUX_CLEAR_CONSOLE_TOOL_NAME,
    title: 'Clear Lux Console',
    description: 'Clears the Unity console through Lux automation.',
    inputSchema: toolInputSchema({
      addConfirmationMessage: { type: 'boolean' },
    }),
    annotations: writeAnnotations(true, false),
  },
  {
    name: LUX_FOCUS_WINDOW_TOOL_NAME,
    title: 'Focus Lux Window',
    description: 'Focuses the Lux or Unity editor window.',
    inputSchema: EMPTY_INPUT_SCHEMA,
    annotations: writeAnnotations(false, true),
  },
  {
    name: LUX_HIERARCHY_TOOL_NAME,
    title: 'Get Lux Hierarchy',
    description: 'Returns Unity hierarchy metadata from the editor.',
    inputSchema: toolInputSchema({
      rootPath: { type: 'string' },
      maxDepth: { type: 'number' },
      includeComponents: { type: 'boolean' },
      includeInactive: { type: 'boolean' },
      includePaths: { type: 'boolean' },
      useComponentsLut: { type: 'boolean' },
      useSelection: { type: 'boolean' },
    }),
    annotations: readOnlyAnnotations(false),
  },
  {
    name: LUX_FIND_GAME_OBJECTS_TOOL_NAME,
    title: 'Find Lux Game Objects',
    description: 'Finds Unity game objects by name, component, tag, layer, or inherited properties.',
    inputSchema: toolInputSchema({
      namePattern: { type: 'string' },
      searchMode: { type: 'string' },
      requiredComponents: { type: 'array', items: { type: 'string' } },
      tag: { type: 'string' },
      layer: { type: 'string' },
      maxResults: { type: 'number' },
      includeInactive: { type: 'boolean' },
      includeInheritedProperties: { type: 'boolean' },
    }),
    annotations: readOnlyAnnotations(false),
  },
  {
    name: LUX_SCREENSHOT_TOOL_NAME,
    title: 'Capture Lux Screenshot',
    description: 'Captures a Unity editor screenshot through Lux automation.',
    inputSchema: toolInputSchema({
      captureMode: { type: 'string' },
      windowName: { type: 'string' },
      resolutionScale: { type: 'number' },
      matchMode: { type: 'string' },
      outputDirectory: { type: 'string' },
      annotateElements: { type: 'boolean' },
      elementsOnly: { type: 'boolean' },
    }),
    annotations: writeAnnotations(false, false),
  },
  {
    name: LUX_PLAY_MODE_TOOL_NAME,
    title: 'Control Lux Play Mode',
    description: 'Controls or reads Unity play mode state.',
    inputSchema: toolInputSchema({
      action: { type: 'string', enum: ['play', 'stop', 'pause', 'resume', 'status'] },
    }, ['action']),
    annotations: writeAnnotations(false, false),
  },
  {
    name: LUX_DYNAMIC_CODE_TOOL_NAME,
    title: 'Execute Lux Dynamic Code',
    description: 'Compiles or executes dynamic C# code in the Unity editor.',
    inputSchema: toolInputSchema({
      code: { type: 'string' },
      file: { type: 'string' },
      parameters: { type: 'object' },
      compileOnly: { type: 'boolean' },
      yieldToForegroundRequests: { type: 'boolean' },
    }),
    annotations: writeAnnotations(true, false),
  },
  {
    name: LUX_MOUSE_UI_TOOL_NAME,
    title: 'Simulate Lux Mouse UI',
    description: 'Simulates mouse actions against Unity UI elements.',
    inputSchema: toolInputSchema({
      action: { type: 'string' },
      x: { type: 'number' },
      y: { type: 'number' },
      fromX: { type: 'number' },
      fromY: { type: 'number' },
      dragSpeed: { type: 'number' },
      durationMs: { type: 'number' },
      button: { type: 'string' },
      bypassRaycast: { type: 'boolean' },
      targetPath: { type: 'string' },
      dropTargetPath: { type: 'string' },
    }, ['action']),
    annotations: writeAnnotations(false, false),
  },
  {
    name: LUX_MOUSE_INPUT_TOOL_NAME,
    title: 'Simulate Lux Mouse Input',
    description: 'Simulates low-level Unity mouse input.',
    inputSchema: toolInputSchema({
      action: { type: 'string' },
      x: { type: 'number' },
      y: { type: 'number' },
      button: { type: 'string' },
      durationMs: { type: 'number' },
      deltaX: { type: 'number' },
      deltaY: { type: 'number' },
      scrollX: { type: 'number' },
      scrollY: { type: 'number' },
      steps: { type: 'number' },
    }, ['action']),
    annotations: writeAnnotations(false, false),
  },
  {
    name: LUX_KEYBOARD_TOOL_NAME,
    title: 'Simulate Lux Keyboard',
    description: 'Simulates keyboard actions in Unity.',
    inputSchema: toolInputSchema({
      action: { type: 'string' },
      key: { type: 'string' },
      durationMs: { type: 'number' },
    }, ['action']),
    annotations: writeAnnotations(false, false),
  },
  {
    name: LUX_RECORD_INPUT_TOOL_NAME,
    title: 'Record Lux Input',
    description: 'Starts or stops Lux input recording.',
    inputSchema: toolInputSchema({
      action: { type: 'string', enum: ['start', 'stop'] },
      outputPath: { type: 'string' },
      keys: { type: 'array', items: { type: 'string' } },
      delaySeconds: { type: 'number' },
      showOverlay: { type: 'boolean' },
    }, ['action']),
    annotations: writeAnnotations(false, false),
  },
  {
    name: LUX_REPLAY_INPUT_TOOL_NAME,
    title: 'Replay Lux Input',
    description: 'Starts, stops, or checks Lux input replay.',
    inputSchema: toolInputSchema({
      action: { type: 'string', enum: ['start', 'stop', 'status'] },
      inputPath: { type: 'string' },
      showOverlay: { type: 'boolean' },
      loop: { type: 'boolean' },
    }, ['action']),
    annotations: writeAnnotations(false, false),
  },
  {
    name: LUX_VERSION_TOOL_NAME,
    title: 'Get Lux Version',
    description: 'Returns the MCP helper server version.',
    inputSchema: EMPTY_INPUT_SCHEMA,
    annotations: readOnlyAnnotations(true),
  },
];

export interface HelperServerConfig {
  projectRoot?: string;
}

export function createServer(config: HelperServerConfig = {}): Server {
  const server = new Server(
    {
      name: SERVER_NAME,
      version: SERVER_VERSION,
    },
    {
      capabilities: {
        tools: {},
      },
    },
  );

  server.setRequestHandler(ListToolsRequestSchema, () => ({
    tools: [
      {
        name: UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME,
        title: 'Unity Selected File Context',
        description: 'Returns Unity editor metadata for the currently selected project files without reading file contents.',
        inputSchema: {
          type: 'object',
          properties: {},
          required: [],
          additionalProperties: false,
        },
        annotations: {
          readOnlyHint: true,
          destructiveHint: false,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
      {
        name: LUX_CONTEXT_TOOL_NAME,
        title: 'Lux Context',
        description: 'Returns Lux package, remote gateway, and automation policy metadata from the Unity editor.',
        inputSchema: {
          type: 'object',
          properties: {},
          required: [],
          additionalProperties: false,
        },
        annotations: {
          readOnlyHint: true,
          destructiveHint: false,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
      {
        name: LUX_EXECUTE_SHELL_TOOL_NAME,
        title: 'Lux Execute Shell',
        description: 'Runs a shell command through Lux automation policy and returns the audited result.',
        inputSchema: automationInputSchema(['commandText']),
        annotations: {
          readOnlyHint: false,
          destructiveHint: true,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
      {
        name: LUX_EXECUTE_GIT_TOOL_NAME,
        title: 'Lux Execute Git',
        description: 'Runs git arguments through Lux automation policy and returns the audited result.',
        inputSchema: automationInputSchema(['gitArguments']),
        annotations: {
          readOnlyHint: false,
          destructiveHint: true,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
      ...LUX_TOOL_DEFINITIONS,
    ],
  }));

  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const toolName = request.params.name;
    if (!isKnownTool(toolName)) {
      throw new McpError(ErrorCode.InvalidParams, `Unknown tool: ${request.params.name}`);
    }

    const projectRoot = toolName === LUX_VERSION_TOOL_NAME ? '' : config.projectRoot ?? findUnityProjectRoot();
    if (toolName !== LUX_VERSION_TOOL_NAME && !projectRoot) {
      throw new McpError(ErrorCode.InvalidRequest, 'Unity project root not found. Set UNITY_PROJECT_PATH or run the MCP helper inside a Unity project.');
    }

    try {
      const payload = await callUnityTool(projectRoot ?? '', toolName, request.params.arguments);

      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(payload),
          },
        ],
      };
    } catch (error) {
      if (error instanceof UnityTcpContextError) {
        throw new McpError(ErrorCode.InternalError, error.message, {
          code: error.code,
          ...toDataObject(error.data),
        });
      }

      throw error;
    }
  });

  return server;
}

function isKnownTool(toolName: string): boolean {
  return toolName === UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME
    || toolName === LUX_CONTEXT_TOOL_NAME
    || toolName === LUX_EXECUTE_SHELL_TOOL_NAME
    || toolName === LUX_EXECUTE_GIT_TOOL_NAME
    || LUX_TOOL_DEFINITIONS.some((tool) => tool.name === toolName);
}

async function callUnityTool(projectRoot: string, toolName: string, argumentsValue: unknown): Promise<unknown> {
  switch (toolName) {
    case UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME:
      return getSelectedFileContextFromUnity(projectRoot);
    case LUX_CONTEXT_TOOL_NAME:
      return getLuxContextFromUnity(projectRoot);
    case LUX_EXECUTE_SHELL_TOOL_NAME:
      return executeLuxShellInUnity(projectRoot, parseAutomationArguments(argumentsValue, 'commandText'));
    case LUX_EXECUTE_GIT_TOOL_NAME:
      return executeLuxGitInUnity(projectRoot, parseAutomationArguments(argumentsValue, 'gitArguments'));
    case LUX_CONSOLE_LOGS_TOOL_NAME:
      return getLuxConsoleLogsFromUnity(projectRoot, parseToolArguments(argumentsValue));
    case LUX_CLEAR_CONSOLE_TOOL_NAME:
      return clearLuxConsoleInUnity(projectRoot, parseToolArguments(argumentsValue));
    case LUX_FOCUS_WINDOW_TOOL_NAME:
      return focusLuxWindowInUnity(projectRoot);
    case LUX_HIERARCHY_TOOL_NAME:
      return getLuxHierarchyFromUnity(projectRoot, parseToolArguments(argumentsValue));
    case LUX_FIND_GAME_OBJECTS_TOOL_NAME:
      return findLuxGameObjectsInUnity(projectRoot, parseToolArguments(argumentsValue));
    case LUX_SCREENSHOT_TOOL_NAME:
      return captureLuxScreenshotInUnity(projectRoot, parseToolArguments(argumentsValue));
    case LUX_PLAY_MODE_TOOL_NAME:
      return controlLuxPlayModeInUnity(projectRoot, parseToolArguments(argumentsValue, 'action'));
    case LUX_DYNAMIC_CODE_TOOL_NAME:
      return executeLuxDynamicCodeInUnity(projectRoot, parseToolArguments(argumentsValue));
    case LUX_MOUSE_UI_TOOL_NAME:
      return simulateLuxMouseUiInUnity(projectRoot, parseToolArguments(argumentsValue, 'action'));
    case LUX_MOUSE_INPUT_TOOL_NAME:
      return simulateLuxMouseInputInUnity(projectRoot, parseToolArguments(argumentsValue, 'action'));
    case LUX_KEYBOARD_TOOL_NAME:
      return simulateLuxKeyboardInUnity(projectRoot, parseToolArguments(argumentsValue, 'action'));
    case LUX_RECORD_INPUT_TOOL_NAME:
      return recordLuxInputInUnity(projectRoot, parseToolArguments(argumentsValue, 'action'));
    case LUX_REPLAY_INPUT_TOOL_NAME:
      return replayLuxInputInUnity(projectRoot, parseToolArguments(argumentsValue, 'action'));
    case LUX_VERSION_TOOL_NAME:
      return { serverName: SERVER_NAME, serverVersion: SERVER_VERSION };
    default:
      throw new McpError(ErrorCode.InvalidParams, `Unknown tool: ${toolName}`);
  }
}

function parseToolArguments(argumentsValue: unknown, requiredField?: string): LuxToolParameters {
  if (argumentsValue === undefined) {
    return {};
  }

  if (!isPlainObject(argumentsValue)) {
    throw new McpError(ErrorCode.InvalidParams, 'Lux tool arguments must be an object.');
  }

  if (requiredField) {
    const requiredValue = argumentsValue[requiredField];
    if (typeof requiredValue !== 'string' || requiredValue.trim().length === 0) {
      throw new McpError(ErrorCode.InvalidParams, `${requiredField} is required.`);
    }
  }

  return argumentsValue;
}

function parseAutomationArguments(argumentsValue: unknown, requiredField: 'commandText' | 'gitArguments'): LuxAutomationRequest {
  if (!isPlainObject(argumentsValue)) {
    throw new McpError(ErrorCode.InvalidParams, 'Lux automation arguments must be an object.');
  }

  const requiredValue = argumentsValue[requiredField];
  if (typeof requiredValue !== 'string' || requiredValue.trim().length === 0) {
    throw new McpError(ErrorCode.InvalidParams, `${requiredField} is required.`);
  }

  return {
    commandText: optionalString(argumentsValue.commandText),
    gitArguments: optionalString(argumentsValue.gitArguments),
    workingDirectory: optionalString(argumentsValue.workingDirectory),
    actor: optionalString(argumentsValue.actor),
    approvalGranted: typeof argumentsValue.approvalGranted === 'boolean' ? argumentsValue.approvalGranted : false,
  };
}

function automationInputSchema(required: Array<'commandText' | 'gitArguments'>): Record<string, unknown> {
  return {
    type: 'object',
    properties: {
      commandText: { type: 'string' },
      gitArguments: { type: 'string' },
      workingDirectory: { type: 'string' },
      actor: { type: 'string' },
      approvalGranted: { type: 'boolean' },
    },
    required,
    additionalProperties: false,
  };
}

function toolInputSchema(properties: Record<string, unknown>, required: string[] = []): Record<string, unknown> {
  return {
    type: 'object',
    properties,
    required,
    additionalProperties: false,
  };
}

function readOnlyAnnotations(idempotentHint: boolean): McpToolDefinition['annotations'] {
  return {
    readOnlyHint: true,
    destructiveHint: false,
    idempotentHint,
    openWorldHint: false,
  };
}

function writeAnnotations(destructiveHint: boolean, idempotentHint: boolean): McpToolDefinition['annotations'] {
  return {
    readOnlyHint: false,
    destructiveHint,
    idempotentHint,
    openWorldHint: false,
  };
}

function optionalString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function toDataObject(data: unknown): Record<string, unknown> {
  return typeof data === 'object' && data !== null ? data as Record<string, unknown> : {};
}

export async function startServer(): Promise<void> {
  const projectRoot = findUnityProjectRoot();

  if (projectRoot) {
    logToStderr(`Unity project root: ${projectRoot}`);
  } else {
    logToStderr('Unity project root not found; continuing with MCP stdio scaffold only.');
  }

  const server = createServer({ projectRoot });
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

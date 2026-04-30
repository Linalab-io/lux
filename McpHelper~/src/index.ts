#!/usr/bin/env node

import { fileURLToPath } from 'node:url';
import { logStartupError } from './logger.js';
import { startServer } from './server.js';

export { logStartupError, logToStderr } from './logger.js';
export { findUnityProjectRoot, isUnityProjectRoot } from './project-root.js';
export { createServer, startServer } from './server.js';

function isMainModule(): boolean {
  if (!process.argv[1]) {
    return false;
  }

  return fileURLToPath(import.meta.url) === process.argv[1];
}

if (isMainModule()) {
  startServer().catch((error: unknown) => {
    logStartupError(error);
    process.exitCode = 1;
  });
}

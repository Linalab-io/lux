import assert from 'node:assert/strict';
import test from 'node:test';
import { logStartupError, logToStderr, type WritableStreamLike } from '../src/logger.js';

function createMemoryStream(): WritableStreamLike & { chunks: string[] } {
  return {
    chunks: [],
    write(chunk: string): true {
      this.chunks.push(chunk);
      return true;
    },
  };
}

test('normal helper logging writes to stderr only', () => {
  const stderr = createMemoryStream();
  const stdout = createMemoryStream();

  logToStderr('project root resolved', { stderr, stdout });

  assert.deepEqual(stdout.chunks, []);
  assert.equal(stderr.chunks.length, 1);
  assert.match(stderr.chunks[0], /project root resolved/);
});

test('startup errors are reported on stderr without stdout noise', () => {
  const stderr = createMemoryStream();
  const stdout = createMemoryStream();

  logStartupError(new Error('boom'), { stderr, stdout });

  assert.deepEqual(stdout.chunks, []);
  assert.equal(stderr.chunks.length, 1);
  assert.match(stderr.chunks[0], /Startup failed/);
  assert.match(stderr.chunks[0], /boom/);
});

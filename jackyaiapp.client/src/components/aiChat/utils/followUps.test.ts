import { describe, expect, it } from 'vitest';

import { followUpsFor, type FollowUpRule } from './followUps';

import type { Message } from '../types';
import type { ToolStep } from './toolActivity';

const step = (name: string, status: ToolStep['status'] = 'done'): ToolStep => ({
  key: name,
  name,
  label: name,
  status,
});

const answer = (patch: Partial<Message> = {}): Message => ({
  id: 'a',
  content: '好的',
  isUser: false,
  timestamp: new Date(0),
  answered: true,
  ...patch,
});

const RULES: FollowUpRule[] = [
  { tools: ['ask_data'], texts: ['改成長條圖', '存到 dashboard'] },
  { tools: ['describe_data'], texts: ['哪個欄位最值得分析？'] },
  { texts: ['還有什麼值得注意的？', '存到 dashboard'] },
];

describe('followUpsFor', () => {
  it('offers the rules of the tools the answer used first, then those for every answer', () => {
    expect(followUpsFor(answer({ toolSteps: [step('ask_data')] }), '各地區營收', RULES)).toEqual([
      '改成長條圖',
      '存到 dashboard',
      '還有什麼值得注意的？',
    ]);
  });

  it('follows the config’s order, not the order the tools ran', () => {
    expect(
      followUpsFor(
        answer({ toolSteps: [step('describe_data'), step('ask_data')] }),
        undefined,
        RULES,
      ),
    ).toEqual(['改成長條圖', '存到 dashboard', '哪個欄位最值得分析？']);
  });

  it('only counts a tool that finished', () => {
    expect(followUpsFor(answer({ toolSteps: [step('ask_data', 'failed')] }), '', RULES)).toEqual([
      '還有什麼值得注意的？',
      '存到 dashboard',
    ]);
  });

  it('never offers what the reader just asked', () => {
    expect(followUpsFor(answer(), ' 存到 dashboard ', RULES)).toEqual(['還有什麼值得注意的？']);
  });

  it('offers nothing under an answer that did not finish, a reopened one, or without rules', () => {
    expect(followUpsFor(answer({ answered: undefined }), '', RULES)).toEqual([]);
    expect(followUpsFor(answer({ isUser: true }), '', RULES)).toEqual([]);
    expect(followUpsFor(answer(), '', undefined)).toEqual([]);
  });
});

import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import DeliveryFeedbackAlert from './DeliveryFeedbackAlert';

import { DeliveryFeedback } from '@/apis/examApis/types';

function render(feedback: DeliveryFeedback) {
  return renderToStaticMarkup(<DeliveryFeedbackAlert feedback={feedback} />);
}

describe('DeliveryFeedbackAlert', () => {
  it('顯示語速、停頓、贅字次數與逐項建議文字', () => {
    const html = render({
      paceWpm: 132.4,
      pauseCount: 2,
      fillerWordCount: 1,
      comments: [
        '語速適中',
        '有 2 次明顯停頓，可以試著先想好整句再開口',
        '出現了 1 次贅字（um/like 等），可以練習減少使用',
      ],
    });
    expect(html).toContain('132.4');
    expect(html).toContain('停頓 2 次');
    expect(html).toContain('贅字 1 次');
    expect(html).toContain('有 2 次明顯停頓');
  });

  it('文案只稱「流暢度」，不使用「發音」字眼，避免誤導為發音評分', () => {
    const html = render({
      paceWpm: 100,
      pauseCount: 0,
      fillerWordCount: 0,
      comments: ['沒有明顯的長停頓'],
    });
    expect(html).toContain('表達流暢度回饋');
    expect(html).not.toContain('發音');
  });
});

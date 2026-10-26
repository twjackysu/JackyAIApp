import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';

import { BRIEFING_PROMPT } from '@/components/StudioAssistant/askStudio';
import AskStudioCard from '@/components/StudioAssistant/AskStudioCard';

/**
 * The home page's way into Studio AI for a signed-in reader: one click asks it for today's briefing
 * (the day's calendar, the words due, the portfolio, together), and the answer opens beside the page.
 * Nothing is asked until it is clicked: the briefing is an AI turn, charged like any other message.
 */
export default function BriefingCard() {
  return (
    <AskStudioCard
      icon={<AutoAwesomeIcon />}
      title="AI 今日簡報"
      description="今天的行程、該複習的單字、資產概況，Studio AI 一次整理好"
      ask={BRIEFING_PROMPT}
    />
  );
}

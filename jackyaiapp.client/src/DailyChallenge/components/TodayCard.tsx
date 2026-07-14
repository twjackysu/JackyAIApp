import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import PsychologyIcon from '@mui/icons-material/Psychology';
import RadioButtonUncheckedIcon from '@mui/icons-material/RadioButtonUnchecked';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import { Link } from 'react-router-dom';

interface TodayCardProps {
  dueCount: number;
  todayChallengeDone: boolean;
}

interface StepProps {
  done: boolean;
  icon: React.ReactNode;
  title: string;
  subtitle: string;
  actionLabel: string;
  to: string;
}

function Step({ done, icon, title, subtitle, actionLabel, to }: StepProps) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, py: 1 }}>
      {done ? (
        <CheckCircleIcon color="success" />
      ) : (
        <RadioButtonUncheckedIcon sx={{ color: 'text.disabled' }} />
      )}
      <Box sx={{ flexGrow: 1, minWidth: 0 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          {icon}
          <Typography
            variant="subtitle2"
            sx={{ fontWeight: 600, textDecoration: done ? 'line-through' : 'none' }}
          >
            {title}
          </Typography>
        </Box>
        <Typography variant="caption" color="text.secondary">
          {subtitle}
        </Typography>
      </Box>
      <Button size="small" variant={done ? 'text' : 'outlined'} component={Link} to={to}>
        {done ? 'Done' : actionLabel}
      </Button>
    </Box>
  );
}

/**
 * A 30-second landing for returning learners: the day's three things. Pure aggregation of
 * existing state — no nagging, just a clear next action.
 */
function TodayCard({ dueCount, todayChallengeDone }: TodayCardProps) {
  const reviewsDone = dueCount === 0;
  const allDone = reviewsDone && todayChallengeDone;

  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 3, borderRadius: 3 }}>
      <Typography variant="subtitle1" sx={{ fontWeight: 700, mb: 0.5 }}>
        Today {allDone && '🎉'}
      </Typography>
      <Typography variant="caption" color="text.secondary">
        {allDone ? "You're all caught up for today. Nice work!" : 'A few minutes keeps it stuck.'}
      </Typography>

      <Box sx={{ mt: 1 }}>
        <Step
          done={reviewsDone}
          icon={<PsychologyIcon fontSize="small" color="action" />}
          title={
            reviewsDone ? 'Reviews cleared' : `Review ${dueCount} word${dueCount > 1 ? 's' : ''}`
          }
          subtitle="Spaced repetition — the words you're about to forget"
          actionLabel="Review"
          to="/english/review"
        />
        <Step
          done={todayChallengeDone}
          icon={<LocalFireDepartmentIcon fontSize="small" color="action" />}
          title="Daily challenge"
          subtitle="5 quick questions to keep your streak"
          actionLabel="Start"
          to="/english/daily"
        />
        <Step
          done={false}
          icon={<AutoAwesomeIcon fontSize="small" color="action" />}
          title="Practice with an exam"
          subtitle="Optional — cloze, translation, sentence, conversation & listening tests (uses credits)"
          actionLabel="Practice"
          to="/english/exam"
        />
      </Box>
    </Paper>
  );
}

export default TodayCard;

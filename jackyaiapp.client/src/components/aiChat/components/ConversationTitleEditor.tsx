import { InputBase } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useRef, useState } from 'react';

import { ACCENT } from '../style';
import { MAX_TITLE_LENGTH, renamedTitle } from '../utils/conversationTitle';

interface ConversationTitleEditorProps {
  title: string;
  /** The title to save, already one line and changed (`renamedTitle`); not called when there is nothing to save. */
  onSave: (title: string) => void;
  /** Editing is over, saved or not. */
  onClose: () => void;
}

/**
 * A conversation's title as a box in its row of the history column (D-144): Enter or leaving the box saves, Escape
 * leaves it as it was (and goes no further, so the drawer around the list stays open).
 */
const ConversationTitleEditor = ({ title, onSave, onClose }: ConversationTitleEditorProps) => {
  const theme = useTheme();
  const [draft, setDraft] = useState(title);
  // Compared with the title the box opened with: the list may be read again meanwhile (the AI naming a new
  // conversation), and leaving the box untouched must not put the old title back over the new one.
  const opened = useRef(title);
  // Enter saves and closes; the blur that may follow must not save again.
  const done = useRef(false);

  const finish = (save: boolean) => {
    if (done.current) return;
    done.current = true;
    const next = save ? renamedTitle(draft, opened.current) : null;
    if (next) onSave(next);
    onClose();
  };

  return (
    <InputBase
      autoFocus
      fullWidth
      value={draft}
      onChange={(e) => setDraft(e.target.value)}
      onFocus={(e) => e.target.select()}
      onBlur={() => finish(true)}
      onKeyDown={(e) => {
        // A key that ends a Chinese input method's composition is the input method's (Safari says 229 for it).
        if (e.nativeEvent.isComposing || e.nativeEvent.keyCode === 229) {
          // Its Escape cancels the composition; it is not for the drawer around the list either.
          if (e.key === 'Escape') e.stopPropagation();
          return;
        }
        if (e.key === 'Enter') {
          e.preventDefault();
          finish(true);
        } else if (e.key === 'Escape') {
          e.stopPropagation();
          finish(false);
        }
      }}
      inputProps={{ 'aria-label': '對話標題', maxLength: MAX_TITLE_LENGTH }}
      sx={{
        fontSize: theme.typography.body2.fontSize,
        px: 1,
        py: 0.25,
        borderRadius: 1.5,
        border: `1px solid ${ACCENT}`,
        bgcolor: 'background.paper',
      }}
    />
  );
};

export default ConversationTitleEditor;

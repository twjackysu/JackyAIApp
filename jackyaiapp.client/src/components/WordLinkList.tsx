import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { Fragment } from 'react';
import { Link as RouterLink } from 'react-router-dom';

import { isLinkableWord } from './wordLinks';

/** Must match `DICTIONARY_PATH` in `Dictionary/index.tsx`. */
const DICTIONARY_PATH = '/english/dictionary';

/** Renders `label: a, b, c` with each linkable word as a real `<a href>` so crawlers and
 * users can walk from one word page to the next. */
function WordLinkList({ label, words }: { label: string; words: string[] }) {
  return (
    <Typography variant="body2">
      {label}:{' '}
      {words.map((w, i) => (
        <Fragment key={`${w}-${i}`}>
          {i > 0 && ', '}
          {isLinkableWord(w) ? (
            <Link
              component={RouterLink}
              to={`${DICTIONARY_PATH}/${encodeURIComponent(w.trim())}`}
              color="inherit"
              underline="hover"
            >
              {w}
            </Link>
          ) : (
            w
          )}
        </Fragment>
      ))}
    </Typography>
  );
}

export default WordLinkList;

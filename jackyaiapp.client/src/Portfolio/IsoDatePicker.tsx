import useMediaQuery from '@mui/material/useMediaQuery';
import { AdapterDayjs } from '@mui/x-date-pickers/AdapterDayjs';
import { DatePicker } from '@mui/x-date-pickers/DatePicker';
import { zhTW } from '@mui/x-date-pickers/locales';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import dayjs, { type Dayjs } from 'dayjs';
import 'dayjs/locale/zh-tw';
import { useState } from 'react';

interface IsoDatePickerProps {
  label: string;
  /** yyyy-MM-dd */
  value: string;
  /** yyyy-MM-dd; dates before it can't be picked. */
  min?: string;
  /** yyyy-MM-dd; dates after it can't be picked. */
  max?: string;
  /** Called only with a complete, in-range date, as yyyy-MM-dd. */
  onChange: (value: string) => void;
  size?: 'small' | 'medium';
  fullWidth?: boolean;
}

const LOCALE_TEXT = zhTW.components.MuiLocalizationProvider.defaultProps.localeText;

/**
 * A date field whose calendar is drawn by the page, taking and returning yyyy-MM-dd strings.
 *
 * Exists because `<TextField type="date">` only wraps the browser's own `<input type="date">`,
 * and on the phone it was tested on, tapping that input opened neither a calendar nor a keyboard
 * — whether a native date picker appears at all is up to the browser, and in-app browsers (LINE,
 * Facebook) are known not to show one. MUI X's DatePicker renders its own calendar, so it opens
 * everywhere. Its header also switches to a year or month view, so reaching a date months back
 * doesn't take one tap per month.
 *
 * On a touch screen a tap anywhere on the field opens the calendar as a dialog, and the field is
 * read-only. Left editable, a tap landed in the year/month/day segments and raised the keyboard
 * to type digits one segment at a time, and only the small calendar icon opened the calendar.
 * With a mouse the field stays typeable, with the icon opening a popover.
 */
export function IsoDatePicker({
  label,
  value,
  min,
  max,
  onChange,
  size = 'small',
  fullWidth,
}: IsoDatePickerProps) {
  // Same test DatePicker itself uses to choose its dialog (touch) or popover (mouse) form.
  const touch = !useMediaQuery('(pointer: fine)', { defaultMatches: true });
  const [open, setOpen] = useState(false);
  // The day tapped in the touch dialog before 確認 / 取消. Held here because `value` belongs to the
  // caller and must not move until the choice is accepted.
  const [draft, setDraft] = useState<Dayjs | null>(null);

  // Only a complete date the picker itself accepts is worth a refetch: typing on desktop passes
  // through partial and out-of-range dates (a year typed digit by digit is briefly year 0002).
  const commit = (next: Dayjs | null, validationError: unknown) => {
    if (next?.isValid() && validationError == null) onChange(next.format('YYYY-MM-DD'));
  };

  return (
    <LocalizationProvider dateAdapter={AdapterDayjs} adapterLocale="zh-tw" localeText={LOCALE_TEXT}>
      <DatePicker
        label={label}
        value={draft ?? dayjs(value)}
        minDate={min ? dayjs(min) : undefined}
        maxDate={max ? dayjs(max) : undefined}
        format="YYYY/MM/DD"
        views={['year', 'month', 'day']}
        openTo="day"
        // The touch dialog changes its value as days are tapped, before 確認 or 取消 — committing
        // on change there would refetch for a day the user then cancelled. So touch keeps those
        // taps as a draft and commits on accept. With a mouse a day click closes the popover and
        // accepts at once, and typing never "accepts", so desktop commits on change.
        onChange={(next, context) =>
          touch ? setDraft(next) : commit(next, context.validationError)
        }
        onAccept={touch ? (next, context) => commit(next, context.validationError) : undefined}
        // Open state is ours only on touch, where a tap anywhere on the read-only field has to
        // open the dialog. With a mouse the picker's own handling (icon button → popover, field
        // typeable) is already what's wanted.
        {...(touch && {
          open,
          onOpen: () => setOpen(true),
          onClose: () => {
            setOpen(false);
            setDraft(null);
          },
        })}
        slotProps={{
          textField: { size, fullWidth, onClick: touch ? () => setOpen(true) : undefined },
          field: touch ? { readOnly: true } : undefined,
          // On touch the whole field opens the calendar, so the icon only takes space — about
          // 36px of a ~143px field on a 375px phone, which left too little for 2026/09/03 and
          // cut the day off. (disableOpenPicker does the same but is deprecated in v9.)
          inputAdornment: touch ? { sx: { display: 'none' } } : undefined,
        }}
      />
    </LocalizationProvider>
  );
}

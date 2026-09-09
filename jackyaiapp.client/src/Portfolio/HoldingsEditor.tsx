import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import { useTheme } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useState } from 'react';

import { AmountRow } from './AmountRow';
import { EditorRow, isRowEmpty, makeRow, rowError, toRow } from './editorTypes';
import { FundRow } from './FundRow';
import { useEditorTour } from './onboardingTour';
import { StockRow } from './StockRow';
import { UpdateHistoryDialog } from './UpdateHistoryDialog';

import { useGetHoldingsQuery, useSaveHoldingsMutation } from '@/apis/portfolioApis';
import { PositionInput } from '@/apis/portfolioApis/types';

const today = () => new Date().toISOString().slice(0, 10);

export function HoldingsEditor() {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [open, setOpen] = useState(false);
  const { data: holdingsRes } = useGetHoldingsQuery();
  const [saveHoldings, { isLoading: isSaving }] = useSaveHoldingsMutation();

  const [effectiveDate, setEffectiveDate] = useState(today());
  const [rows, setRows] = useState<EditorRow[]>([]);
  const [submitted, setSubmitted] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const hasExisting = (holdingsRes?.data?.positions?.length ?? 0) > 0;

  // First-open walkthrough of every editor section — see onboardingTour.ts.
  useEditorTour(open);

  // Open the editor seeded from the latest holdings. Called directly from the trigger button
  // (not a useEffect keyed on `open`) so it can't race with loadFromSnapshot below — both used
  // to set rows via an effect that fired whenever `open` became true, so loading a past snapshot
  // (which also sets `open`) got its rows immediately clobbered back to the latest holdings.
  const openWithLatestHoldings = () => {
    setEffectiveDate(today()); // new update defaults to today
    setRows((holdingsRes?.data?.positions ?? []).map(toRow));
    setSubmitted(false);
    setSaveError(null);
    setOpen(true);
  };

  // Load an arbitrary past snapshot's positions into the editor (restore flow). Defaults the date
  // to the snapshot's OWN effective date, not today — saving as-is then corrects/overwrites that
  // exact historical record in place, which is what "載入 an old snapshot" most often means (fix a
  // data-entry mistake in the past). Previously this always reset to today, so an edit meant to
  // fix an old record silently created a brand-new snapshot dated today instead — the old (wrong)
  // record was left untouched and the fix landed on the wrong date with no visible error. The date
  // field is still a plain editable TextField, so explicitly changing it to today still gives the
  // "copy this old snapshot forward as a fresh update" flow the UI copy already describes.
  const loadFromSnapshot = (positions: PositionInput[], effectiveDate: string) => {
    setEffectiveDate(effectiveDate || today());
    setRows(positions.map(toRow));
    setSubmitted(false);
    setSaveError(null);
    setOpen(true);
  };

  // Stocks are split by market so the user picks a section instead of a per-row market dropdown.
  const twStocks = rows.filter((r) => r.assetType === 'STOCK' && (r.market || 'TW') !== 'US');
  const usStocks = rows.filter((r) => r.assetType === 'STOCK' && r.market === 'US');
  const cashes = rows.filter((r) => r.assetType === 'CASH');
  const liabilities = rows.filter((r) => r.assetType === 'LIABILITY');
  const realEstates = rows.filter((r) => r.assetType === 'REAL_ESTATE');
  const funds = rows.filter((r) => r.assetType === 'FUND');

  const patchRow = (key: string, patch: Partial<EditorRow>) =>
    setRows((rs) => rs.map((r) => (r._key === key ? { ...r, ...patch } : r)));
  const removeRow = (key: string) => setRows((rs) => rs.filter((r) => r._key !== key));
  const addRow = (type: 'STOCK' | 'CASH' | 'LIABILITY' | 'REAL_ESTATE' | 'FUND', market?: string) =>
    setRows((rs) => [...rs, makeRow(type, market)]);

  // Rows the user actually started filling in (blank ones are dropped silently).
  const meaningfulRows = rows.filter((r) => !isRowEmpty(r));
  const invalidRows = meaningfulRows.filter((r) => rowError(r) !== null);
  const canSave = meaningfulRows.length > 0 && invalidRows.length === 0;

  const handleSave = async () => {
    setSubmitted(true);
    setSaveError(null);
    if (!canSave) return;
    const positions = meaningfulRows.map(({ _key, ...rest }) => rest); // eslint-disable-line @typescript-eslint/no-unused-vars
    try {
      // unwrap() so a failed save (network/server error) throws instead of silently
      // resolving — without it the dialog closed regardless of outcome, so a failed
      // save looked indistinguishable from "saved but summary didn't update".
      await saveHoldings({ effectiveDate, positions }).unwrap();
      setOpen(false);
    } catch {
      setSaveError('儲存失敗，請確認網路連線後再試一次');
    }
  };

  return (
    <>
      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
        {hasExisting && <UpdateHistoryDialog onLoadSnapshot={loadFromSnapshot} />}
        <Button
          data-tour="portfolio-tour-edit"
          variant="contained"
          startIcon={hasExisting ? <EditIcon /> : <AddIcon />}
          onClick={openWithLatestHoldings}
        >
          {hasExisting ? '更新持倉' : '建立持倉'}
        </Button>
      </Box>

      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        maxWidth="md"
        fullWidth
        fullScreen={fullScreen}
      >
        <DialogTitle>更新持倉</DialogTitle>
        <DialogContent
          sx={{ display: 'flex', flexDirection: 'column', gap: 3, pt: '16px !important' }}
        >
          {submitted && !canSave && (
            <Alert severity="error">
              {meaningfulRows.length === 0
                ? '請至少填入一筆持倉'
                : '有欄位未填完整，請確認標紅的項目'}
            </Alert>
          )}
          {saveError && <Alert severity="error">{saveError}</Alert>}

          <Box data-tour="portfolio-tour-editor-date">
            <TextField
              size="small"
              type="date"
              label="生效日期"
              value={effectiveDate}
              onChange={(e) => setEffectiveDate(e.target.value)}
              slotProps={{ inputLabel: { shrink: true } }}
              helperText="這批持倉從這天起算，直到下次更新"
            />
          </Box>

          {/* TW stocks */}
          <Section
            title="台股"
            onAdd={() => addRow('STOCK', 'TW')}
            addLabel="新增台股"
            empty={twStocks.length === 0}
            tourId="portfolio-tour-editor-tw"
          >
            {twStocks.map((r) => (
              <StockRow
                key={r._key}
                row={r}
                error={submitted ? rowError(r) : null}
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
          </Section>

          <Divider />

          {/* US stocks */}
          <Section
            title="美股"
            onAdd={() => addRow('STOCK', 'US')}
            addLabel="新增美股"
            empty={usStocks.length === 0}
            tourId="portfolio-tour-editor-us"
          >
            {usStocks.map((r) => (
              <StockRow
                key={r._key}
                row={r}
                error={submitted ? rowError(r) : null}
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
          </Section>

          <Divider />

          {/* Cash */}
          <Section
            title="現金"
            onAdd={() => addRow('CASH')}
            addLabel="新增現金"
            empty={cashes.length === 0}
            tourId="portfolio-tour-editor-cash"
          >
            {cashes.map((r) => (
              <AmountRow
                key={r._key}
                row={r}
                error={submitted ? rowError(r) : null}
                amountLabel="金額"
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
          </Section>

          <Divider />

          {/* Liabilities — any named debt (信貸/房貸/...); no restriction on what it represents. */}
          <Section
            title="負債"
            onAdd={() => addRow('LIABILITY')}
            addLabel="新增負債"
            empty={liabilities.length === 0}
            tourId="portfolio-tour-editor-liability"
          >
            {liabilities.map((r) => (
              <AmountRow
                key={r._key}
                row={r}
                error={submitted ? rowError(r) : null}
                amountLabel="金額"
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
            {/* 負債本身不計入市場曝險（PortfolioExposure.Factor），但仍會拉低分母（淨值）——
                若債務對應的資產（房子）沒有一併記錄在下方「不動產」，分母少了對應的抵銷項，
                曝險比例就會虛高，容易誤判槓桿程度。 */}
            {liabilities.length > 0 && realEstates.length === 0 && (
              <Alert severity="warning" sx={{ mt: 1 }}>
                有負債但沒有記錄對應的不動產：若這筆負債（如房貸、增貸）是用來取得房產，
                建議一併在下方「不動產」記錄，否則市場曝險比例會被拉高、失真。
              </Alert>
            )}
          </Section>

          <Divider />

          {/* Real estate — manually valued (no live price feed for TW property); counts toward net
              worth but never toward market exposure (PortfolioExposure.Factor), so a large home-
              equity loan against it doesn't make the exposure ratio look more leveraged than it is. */}
          <Section
            title="不動產"
            onAdd={() => addRow('REAL_ESTATE')}
            addLabel="新增不動產"
            empty={realEstates.length === 0}
            tourId="portfolio-tour-editor-realestate"
          >
            {realEstates.map((r) => (
              <AmountRow
                key={r._key}
                row={r}
                error={submitted ? rowError(r) : null}
                amountLabel="價值"
                showValuationMethod
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
          </Section>

          <Divider />

          {/* Funds */}
          <Section
            title="基金"
            onAdd={() => addRow('FUND')}
            addLabel="新增基金"
            empty={funds.length === 0}
            tourId="portfolio-tour-editor-fund"
          >
            {funds.map((r) => (
              <FundRow
                key={r._key}
                row={r}
                error={submitted ? rowError(r) : null}
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
          </Section>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>取消</Button>
          <Button
            data-tour="portfolio-tour-editor-save"
            variant="contained"
            onClick={handleSave}
            disabled={isSaving}
          >
            {isSaving ? '儲存中...' : '儲存快照'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function Section({
  title,
  onAdd,
  addLabel,
  empty,
  tourId,
  children,
}: {
  title: string;
  onAdd: () => void;
  addLabel: string;
  empty: boolean;
  tourId?: string;
  children: React.ReactNode;
}) {
  return (
    <Box data-tour={tourId} sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
          {title}
        </Typography>
        <Button size="small" startIcon={<AddIcon />} onClick={onAdd}>
          {addLabel}
        </Button>
      </Box>
      {empty ? (
        <Typography variant="caption" color="text.secondary">
          尚無項目
        </Typography>
      ) : (
        children
      )}
    </Box>
  );
}

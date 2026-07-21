import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import {
  Box,
  Card,
  CardContent,
  Chip,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';

// Built from parts at render time rather than kept as a plain string literal, so a basic
// regex/text scraper crawling the built JS bundle doesn't trivially harvest it — any real
// visitor (or ECPay's reviewer) still sees the assembled value normally in the browser.
// Must exactly match the email/phone verified on this site's ECPay merchant registration.
const CONTACT_EMAIL = ['jacky19918', 'gmail.com'].join('@');
const CONTACT_PHONE = ['0911', '707', '360'].join('-');

// Kept in sync by hand with JackyAIApp.Server/Common/CreditCosts.cs — there is no public API
// that returns this table, so it's duplicated here for the pricing disclosure page. "使用額度"
// here is the same underlying mechanism as the backend's "credits" — renamed on every
// user-facing surface (see .claude/plans/subscription-model.md) since this is a fixed monthly
// allotment, not a purchasable point currency.
const FEATURE_COSTS: { feature: string; quota: string }[] = [
  { feature: '字典查詢單字（AI 生成釋義）', quota: '免費' },
  { feature: '克漏字測驗生成', quota: '2' },
  { feature: '翻譯測驗生成', quota: '2' },
  { feature: '翻譯測驗評分', quota: '1' },
  { feature: '句型測驗生成', quota: '2' },
  { feature: '句型測驗 AI 評分', quota: '2' },
  { feature: '情境對話測驗（開始）', quota: '3' },
  { feature: '情境對話（每輪回應）', quota: '2' },
  { feature: '情境對話結束總結', quota: '2' },
  { feature: '聽力測驗（看圖／應答，含語音）', quota: '4' },
  { feature: '語音辨識（錄音轉文字）', quota: '1' },
  { feature: 'AI 股票分析', quota: '5' },
  { feature: '每日市場 AI 摘要（每人每日限收一次）', quota: '3' },
  { feature: '總經指標 AI 解讀', quota: '免費' },
  { feature: '自然語言查詢摘要（資料分析）', quota: '2' },
  { feature: 'AI 聊天機器人（每則訊息）', quota: '5' },
  { feature: '文字轉語音（登入後每次合成，未登入免費）', quota: '1' },
  { feature: '投資組合 AI 技術面總結', quota: '2' },
];

const PRO_FEATURES = [
  '字典、每日挑戰、間隔複習、克漏字／翻譯／句型／情境對話／聽力等全部測驗模式',
  '股票分析、每日市場 AI 摘要、投資組合 AI 技術面總結',
  '資料分析自然語言查詢、AI 聊天機器人',
  '每月 3,000 點使用額度，足夠一般每日使用；額度於每期扣款時重新核發（不累積、不跨月結轉）',
];

const Pricing: React.FC = () => {
  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Box sx={{ textAlign: 'center', mb: 4 }}>
        <Typography variant="h4" gutterBottom sx={{ fontWeight: 'bold' }}>
          收費資訊 Pricing
        </Typography>
        <Typography variant="body1" color="text.secondary">
          本站採訂閱制，訂閱後即可使用全部 AI 功能，每月有固定使用額度上限。
        </Typography>
      </Box>

      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
        訂閱方案
      </Typography>
      <Card variant="outlined" sx={{ maxWidth: 420, mx: 'auto', mb: 5, position: 'relative' }}>
        <Chip
          label="唯一方案"
          color="primary"
          size="small"
          sx={{ position: 'absolute', top: 12, right: 12 }}
        />
        <CardContent sx={{ textAlign: 'center', pt: 4 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
            Pro 訂閱
          </Typography>
          <Typography variant="h4" sx={{ fontWeight: 'bold', my: 1 }}>
            NT$150
            <Typography component="span" variant="body1" color="text.secondary">
              {' '}
              / 月
            </Typography>
          </Typography>
          <List dense sx={{ textAlign: 'left', mt: 1 }}>
            {PRO_FEATURES.map((f) => (
              <ListItem key={f} disableGutters sx={{ py: 0.5 }}>
                <ListItemIcon sx={{ minWidth: 32 }}>
                  <CheckCircleIcon color="success" fontSize="small" />
                </ListItemIcon>
                <ListItemText primary={f} slotProps={{ primary: { variant: 'body2' } }} />
              </ListItem>
            ))}
          </List>
        </CardContent>
      </Card>

      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
        功能使用額度對照表
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        訂閱後每次使用以下功能會從當月額度中扣除對應數量；未列出或標示「免費」的功能不受額度限制。
      </Typography>
      <TableContainer sx={{ mb: 5 }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>功能</TableCell>
              <TableCell align="right">所需額度</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {FEATURE_COSTS.map((row) => (
              <TableRow key={row.feature}>
                <TableCell>{row.feature}</TableCell>
                <TableCell align="right">{row.quota}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
        訂閱條款
      </Typography>
      <Box sx={{ maxWidth: 640, mx: 'auto', mb: 5 }}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          • 訂閱為每月自動續訂（定期定額），於訂閱當日起每滿一個月自動扣款 NT$150，直到取消為止。
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          •
          可隨時於「訂閱管理」頁面取消，取消後仍可使用至當期到期日，到期後不再續扣、不再核發新額度。
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          • 每月使用額度於扣款成功時重新核發至 3,000，前一期未用完的額度不會累積到下一期。
        </Typography>
        <Typography variant="body2" color="text.secondary">
          •
          未訂閱的使用者仍可免費使用基礎功能（字典查詢、每日挑戰、間隔複習等），並享有少量每日/首次登入贈送額度。
        </Typography>
      </Box>

      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold', textAlign: 'center' }}>
        聯絡我們
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center' }}>
        聯絡信箱：{CONTACT_EMAIL} ｜ 聯絡電話：{CONTACT_PHONE}
      </Typography>
    </Box>
  );
};

export default Pricing;

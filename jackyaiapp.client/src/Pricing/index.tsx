import {
  Box,
  CircularProgress,
  Grid,
  Card,
  CardContent,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';

import { useGetECPayPacksQuery } from '@/apis/ecpayApis';
import { fmtNum } from '@/utils/format';

// Built from parts at render time rather than kept as a plain string literal, so a basic
// regex/text scraper crawling the built JS bundle doesn't trivially harvest it — any real
// visitor (or ECPay's reviewer) still sees the assembled value normally in the browser.
// Must exactly match the email/phone verified on this site's ECPay merchant registration.
const CONTACT_EMAIL = ['jacky19918', 'gmail.com'].join('@');
const CONTACT_PHONE = ['0911', '707', '360'].join('-');

// Kept in sync by hand with JackyAIApp.Server/Common/CreditCosts.cs — there is no public API
// that returns this table, so it's duplicated here for the pricing disclosure page.
const FEATURE_COSTS: { feature: string; credits: string }[] = [
  { feature: '字典查詢單字（AI生成釋義）', credits: '免費' },
  { feature: '克漏字測驗生成', credits: '2' },
  { feature: '翻譯測驗生成', credits: '2' },
  { feature: '翻譯測驗評分', credits: '1' },
  { feature: '句型測驗生成', credits: '2' },
  { feature: '句型測驗 AI 評分', credits: '2' },
  { feature: '情境對話測驗（開始）', credits: '3' },
  { feature: '情境對話（每輪回應）', credits: '2' },
  { feature: '情境對話結束總結', credits: '2' },
  { feature: '語音辨識（錄音轉文字）', credits: '1' },
  { feature: 'AI 股票分析', credits: '5' },
  { feature: '每日市場 AI 摘要（每人每日限收一次）', credits: '3' },
  { feature: '總經指標 AI 解讀', credits: '免費' },
  { feature: '自然語言查詢摘要（資料分析）', credits: '2' },
  { feature: 'AI 聊天機器人（每則訊息）', credits: '5' },
  { feature: '文字轉語音（登入後每次合成，未登入免費）', credits: '1' },
  { feature: '投資組合 AI 技術面總結', credits: '2' },
];

const Pricing: React.FC = () => {
  const { data: packs, isLoading } = useGetECPayPacksQuery();

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Box sx={{ textAlign: 'center', mb: 4 }}>
        <Typography variant="h4" gutterBottom sx={{ fontWeight: 'bold' }}>
          收費資訊 Pricing
        </Typography>
        <Typography variant="body1" color="text.secondary">
          本站部分 AI 功能採 Credit 點數制，點數可透過綠界 ECPay 或 PayPal 購買，購買後立即入帳。
        </Typography>
      </Box>

      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
        點數購買方案
      </Typography>
      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
          <CircularProgress />
        </Box>
      ) : (
        <Grid container spacing={2} sx={{ mb: 5 }}>
          {packs?.map((pack) => (
            <Grid key={pack.id} size={{ xs: 12, sm: 4 }}>
              <Card variant="outlined" sx={{ height: '100%', position: 'relative' }}>
                {pack.badge && (
                  <Chip
                    label={pack.badge}
                    color="primary"
                    size="small"
                    sx={{ position: 'absolute', top: 10, right: 10 }}
                  />
                )}
                <CardContent sx={{ textAlign: 'center' }}>
                  <Typography variant="subtitle1">{pack.name}</Typography>
                  <Typography variant="h5" sx={{ fontWeight: 'bold', my: 1 }}>
                    NT${pack.priceTWD}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {fmtNum(pack.credits)} credits
                  </Typography>
                </CardContent>
              </Card>
            </Grid>
          ))}
        </Grid>
      )}

      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
        功能扣點對照表
      </Typography>
      <TableContainer sx={{ mb: 5 }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>功能</TableCell>
              <TableCell align="right">所需 Credits</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {FEATURE_COSTS.map((row) => (
              <TableRow key={row.feature}>
                <TableCell>{row.feature}</TableCell>
                <TableCell align="right">{row.credits}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center', mb: 4 }}>
        點數為單一用途消費點數，僅可用於本站服務，不可轉讓、不可折現，購買後立即入帳，無使用期限。
      </Typography>

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

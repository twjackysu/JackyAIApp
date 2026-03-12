# JackyAIApp Monetization Plan

_Created: 2026-03-12 | Owner: JackyOpenClaw 🦞_
_Goal: Cover token costs + 20% margin_

## Current State Analysis

### What the app has
- **Finance**: TW stock analysis with AI insights (free, no login)
- **English Learning**: Dictionary, Exams (conversation/cloze/translation), Repository (login required)
- **PDF Unlocker**: Free tool, no login
- **Connectors**: OAuth integration (Microsoft, Google, Atlassian)
- **Credit System**: Already built! 200 free credits per user, consume/add logic, transaction history
- **Auth**: Google OAuth login
- **Hosting**: Azure App Service (jackyai.azurewebsites.net)

### What's missing for monetization
- No payment gateway integration
- No pricing page
- No credit purchase UI
- No premium/free tier distinction
- No ads

## Strategy: Credit-Based Freemium

The credit system already exists. The simplest path to revenue:

1. **Free tier**: 200 credits on signup (already done)
2. **Credit packs**: Buy more credits via payment
3. **Credit gates**: AI-powered features consume credits

### Revenue Target Calculation
- Assume token costs ≈ $20–50/month (Azure OpenAI usage)
- Target: costs × 1.2 = $24–60/month
- At $1 = 100 credits, need ~3-6 paying users buying $10 packs/month

## Implementation Phases

### Phase 1: Credit Purchase (Week 1-2) ✅ Priority
- [ ] Integrate Stripe Checkout (simplest payment flow)
- [ ] Create pricing page with credit pack options
- [ ] Credit packs: $5 (500 credits), $10 (1100 credits), $20 (2400 credits)
- [ ] Stripe webhook → `AddCreditsAsync` on successful payment
- [ ] Purchase history in user profile

### Phase 2: Credit Consumption Gates (Week 2-3)
- [ ] AI Stock Analysis → 5 credits per analysis
- [ ] AI Conversation Test → 3 credits per session
- [ ] AI Exam Generation → 2 credits per exam
- [ ] Dictionary AI lookup → 1 credit per word
- [ ] Show remaining credits in navbar
- [ ] "Insufficient credits" modal → link to pricing page

### Phase 3: Growth & Conversion (Week 3-4)
- [ ] Landing page improvements (SEO, value proposition)
- [ ] "Credits running low" notification
- [ ] Referral bonus: invite friend → both get 50 credits
- [ ] Daily free credits (10/day) to keep users engaged
- [ ] Google AdSense on free pages (Finance, PDF) as secondary revenue

## Technical Decisions

### Why Stripe?
- Lowest integration effort (Checkout Session = ~50 lines of code)
- No PCI compliance burden (Stripe handles all card data)
- Supports TW credit cards
- Free to set up, 2.9% + $0.30 per transaction

### Credit Pricing Rationale
| Pack | Price | Credits | Bonus | Per Credit |
|------|-------|---------|-------|------------|
| Starter | $5 | 500 | 0% | $0.010 |
| Popular | $10 | 1,100 | 10% | $0.009 |
| Best Value | $20 | 2,400 | 20% | $0.008 |

### Cost per AI Feature (estimated)
| Feature | OpenAI Cost | Credits Charged | Margin |
|---------|-------------|-----------------|--------|
| Stock Analysis (GPT-4o-mini) | ~$0.01 | 5 ($0.05) | 5x |
| Conversation (GPT-4o-mini + Whisper + TTS) | ~$0.03 | 3 ($0.03) | 1x |
| Dictionary Lookup | ~$0.005 | 1 ($0.01) | 2x |
| Exam Generation | ~$0.01 | 2 ($0.02) | 2x |

## Progress Log

### 2026-03-12
- [x] Analyzed existing codebase
- [x] Created monetization plan
- [ ] Starting Phase 1 implementation

---

_This plan is reviewed and updated on each heartbeat cycle._

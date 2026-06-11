export interface ScenarioTemplate {
  id: string;
  name: string;
  scenario: string;
  userRole: string;
  aiRole: string;
  description: string;
  tags: string[];
}

export const SCENARIO_TEMPLATES: ScenarioTemplate[] = [
  {
    id: 'restaurant',
    name: '餐廳點餐',
    scenario: 'A customer ordering food at a restaurant',
    userRole: 'Customer',
    aiRole: 'Waiter/Waitress',
    description: '練習在餐廳點餐的日常對話',
    tags: ['日常', '食物', '服務'],
  },
  {
    id: 'hotel',
    name: '酒店入住',
    scenario: 'A guest checking into a hotel',
    userRole: 'Guest',
    aiRole: 'Hotel Receptionist',
    description: '學習酒店入住登記相關英語',
    tags: ['旅遊', '住宿', '服務'],
  },
  {
    id: 'shopping',
    name: '購物詢問',
    scenario: 'A customer asking about products in a store',
    userRole: 'Customer',
    aiRole: 'Shop Assistant',
    description: '練習購物時的詢問和討論',
    tags: ['購物', '商品', '價格'],
  },
  {
    id: 'interview',
    name: '工作面試',
    scenario: 'A job interview conversation',
    userRole: 'Job Candidate',
    aiRole: 'Interviewer',
    description: '模擬工作面試情境',
    tags: ['工作', '面試', '專業'],
  },
  {
    id: 'doctor',
    name: '看醫生',
    scenario: 'A patient visiting a doctor',
    userRole: 'Patient',
    aiRole: 'Doctor',
    description: '學習看病時的醫療英語',
    tags: ['醫療', '健康', '症狀'],
  },
  {
    id: 'airport',
    name: '機場問路',
    scenario: 'A traveler asking for directions at an airport',
    userRole: 'Traveler',
    aiRole: 'Airport Staff',
    description: '練習在機場問路和查詢資訊',
    tags: ['旅遊', '交通', '方向'],
  },
];

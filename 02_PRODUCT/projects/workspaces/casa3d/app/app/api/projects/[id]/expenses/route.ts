import { handle } from '@/lib/http';
import { getExpenses, addExpense, deleteExpense } from '@/lib/expenses';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getExpenses(o, id)); };
// body: { date: 'AAAA-LL-ZZ', category, amount, supplier?, doc?, note? }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addExpense(o, id, await readJsonLimited(req, 4096)), 201); };
export const DELETE = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(o => deleteExpense(o, id, new URL(req.url).searchParams.get('entry'))); };

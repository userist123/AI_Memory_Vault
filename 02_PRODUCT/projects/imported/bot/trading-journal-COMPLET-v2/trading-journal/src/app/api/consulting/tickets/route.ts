import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { getCurrentUser } from '@/lib/auth/session';
import {
  createTicket,
  getUserTickets,
  getAllTickets,
  getTicket,
  addMessageToTicket,
  updateTicketStatus,
} from '@/lib/db/consulting';

export const runtime = 'nodejs';

const CreateTicketSchema = z.object({
  subject: z.string().min(3).max(200),
  message: z.string().min(10).max(5000),
  category: z.enum(['fiscal', 'trading', 'technical', 'billing', 'other']),
  priority: z.enum(['low', 'normal', 'high', 'urgent']).default('normal'),
});

const ReplySchema = z.object({
  ticketId: z.string().min(1),
  message: z.string().min(1).max(5000),
});

// ===== Get tickets =====
export async function GET(req: NextRequest) {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const { searchParams } = new URL(req.url);
  const ticketId = searchParams.get('id');
  const adminMode = searchParams.get('admin') === 'true';

  if (ticketId) {
    const ticket = await getTicket(ticketId);
    if (!ticket) return NextResponse.json({ error: 'Not found' }, { status: 404 });

    // Check ownership (unless admin)
    const ownerEmail = process.env.OWNER_EMAIL;
    const isOwner = ownerEmail && user.email === ownerEmail;
    if (ticket.userId !== user._id && !isOwner) {
      return NextResponse.json({ error: 'Not yours' }, { status: 403 });
    }
    return NextResponse.json({ ticket });
  }

  if (adminMode) {
    const ownerEmail = process.env.OWNER_EMAIL;
    if (!ownerEmail || user.email !== ownerEmail) {
      return NextResponse.json({ error: 'Admin only' }, { status: 403 });
    }
    const tickets = await getAllTickets({ limit: 200 });
    return NextResponse.json({ tickets });
  }

  const tickets = await getUserTickets(user._id!);
  return NextResponse.json({ tickets });
}

// ===== Create ticket =====
export async function POST(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    const body = await req.json();
    const parsed = CreateTicketSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json({ error: 'Invalid', details: parsed.error.errors }, { status: 400 });
    }

    const ticketId = await createTicket({
      userId: user._id!,
      userEmail: user.email,
      userName: user.name,
      userPlan: user.plan,
      subject: parsed.data.subject,
      category: parsed.data.category,
      priority: parsed.data.priority,
      messages: [{
        from: 'user',
        content: parsed.data.message,
        createdAt: new Date(),
      }],
      status: 'waiting_owner',
    });

    // TODO: notify owner via email
    const ownerEmail = process.env.OWNER_EMAIL;
    if (ownerEmail) {
      const { sendEmail } = await import('@/lib/notifications/email');
      await sendEmail({
        to: ownerEmail,
        subject: `[Ticket nou ${parsed.data.priority}] ${parsed.data.subject}`,
        html: `<p>De la: <strong>${user.email}</strong> (${user.plan})</p><p>${parsed.data.message.replace(/\n/g, '<br>')}</p><p><a href="${process.env.NEXT_PUBLIC_APP_URL}/admin/tickets/${ticketId}">Deschide ticket</a></p>`,
      });
    }

    return NextResponse.json({ ticketId });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Tickets] Create error:', e);
    return NextResponse.json({ error: 'Create failed', details: e.message }, { status: 500 });
  }
}

// ===== Reply =====
export async function PATCH(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    const body = await req.json();
    const parsed = ReplySchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json({ error: 'Invalid', details: parsed.error.errors }, { status: 400 });
    }

    const ticket = await getTicket(parsed.data.ticketId);
    if (!ticket) return NextResponse.json({ error: 'Not found' }, { status: 404 });

    const ownerEmail = process.env.OWNER_EMAIL;
    const isOwner = ownerEmail && user.email === ownerEmail;
    const isUserOwnerOfTicket = ticket.userId === user._id;

    if (!isOwner && !isUserOwnerOfTicket) {
      return NextResponse.json({ error: 'Not yours' }, { status: 403 });
    }

    await addMessageToTicket(
      parsed.data.ticketId,
      isOwner ? 'owner' : 'user',
      parsed.data.message
    );

    // If owner replied, notify user via email
    if (isOwner) {
      const { sendEmail } = await import('@/lib/notifications/email');
      await sendEmail({
        to: ticket.userEmail,
        subject: `[Re: ${ticket.subject}] Răspuns primit`,
        html: `<p>Ai primit un răspuns la ticket-ul tău:</p><blockquote>${parsed.data.message.replace(/\n/g, '<br>')}</blockquote><p><a href="${process.env.NEXT_PUBLIC_APP_URL}/consulting">Vezi ticket</a></p>`,
      });
    }

    return NextResponse.json({ success: true });
  } catch (err: unknown) {
    const e = err as { message?: string };
    return NextResponse.json({ error: 'Reply failed', details: e.message }, { status: 500 });
  }
}

// ===== Update status =====
export async function PUT(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    const body = await req.json();
    const { ticketId, status } = body;
    if (!ticketId || !status) {
      return NextResponse.json({ error: 'ticketId and status required' }, { status: 400 });
    }

    const ticket = await getTicket(ticketId);
    if (!ticket) return NextResponse.json({ error: 'Not found' }, { status: 404 });

    const ownerEmail = process.env.OWNER_EMAIL;
    const isOwner = ownerEmail && user.email === ownerEmail;

    // Only owner can set to resolved/closed. User can cancel (close) their own.
    if ((status === 'resolved') && !isOwner) {
      return NextResponse.json({ error: 'Only owner can resolve' }, { status: 403 });
    }

    if (ticket.userId !== user._id && !isOwner) {
      return NextResponse.json({ error: 'Not yours' }, { status: 403 });
    }

    await updateTicketStatus(ticketId, status);
    return NextResponse.json({ success: true });
  } catch (err: unknown) {
    const e = err as { message?: string };
    return NextResponse.json({ error: 'Update failed', details: e.message }, { status: 500 });
  }
}

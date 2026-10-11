import { NextResponse, type NextRequest } from 'next/server';
import { isLocalRequest, isPublicPath } from './lib/public-gate';
// Din afară (prin tunel) se văd doar linkurile pentru client; restul răspunde 404, ca și cum n-ar exista.
export function middleware(req: NextRequest){
  if (isLocalRequest(req.headers.get('host'), req.headers) || isPublicPath(req.nextUrl.pathname)) return NextResponse.next();
  return new NextResponse('Not found', { status: 404, headers: { 'x-robots-tag': 'noindex', 'cache-control': 'no-store' } });
}

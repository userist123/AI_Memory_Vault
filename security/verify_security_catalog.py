from __future__ import annotations
import hashlib,json,os,subprocess,sys
from pathlib import Path
from urllib.parse import urlparse
from packaging.version import Version
ROOT=Path(__file__).resolve().parents[1]
KEY=ROOT/'security/catalog-signing-public-key.pem'
ALLOWED=ROOT/'security/AI_SECURITY_UPDATE_ALLOWED_HOSTS.txt'
def alert(msg): print('::error::'+msg)
def host(url):
 p=urlparse(url)
 if p.scheme!='https' or not p.hostname: raise ValueError('HTTPS URL required')
 return p.hostname.lower()
def allowed_host(h):
 return h in {x.strip().lower() for x in ALLOWED.read_text().splitlines() if x.strip() and not x.startswith('#')}
def invalid(msg): alert(msg); return 0
def main():
 url=os.environ.get('CATALOG_URL','').strip()
 if not url: print('::warning::AI_SECURITY_UPDATE_CATALOG_URL is not configured'); return 0
 try:
  h=host(url)
  if not allowed_host(h): return invalid('Catalog host is not explicitly allowlisted')
  sig_url=os.environ.get('CATALOG_SIGNATURE_URL','').strip()
  if not sig_url: return invalid('Catalog signature URL is not configured')
  if host(sig_url)!=h: return invalid('Catalog signature host differs from approved catalog host')
  if not KEY.is_file() or KEY.stat().st_size==0: return invalid('Catalog signing public key is missing; manual trust-anchor configuration is required')
  raw=Path('/tmp/security-update-catalog.json').read_bytes()
  subprocess.run(['curl','--fail','--location','--silent','--show-error','--max-time','30',sig_url,'-o','/tmp/security-update-catalog.sig'],check=True)
  subprocess.run(['openssl','dgst','-sha256','-verify',str(KEY),'-signature','/tmp/security-update-catalog.sig','/tmp/security-update-catalog.json'],check=True)
  data=json.loads(raw.decode('utf-8'))
  if data.get('schema_version')!=1 or 'minimum_runtime_version' not in data: return invalid('Catalog schema is invalid after signature verification')
  Version(str(data['minimum_runtime_version']))
  for item in data.get('updates',[]):
   for k in ('update_id','version','severity','released_at','min_runtime_version','package_sha256','package_url'): 
    if k not in item: return invalid('Signed catalog entry is missing '+k)
   Version(str(item['version'])); Version(str(item['min_runtime_version']))
   ph=host(str(item['package_url']))
   if not allowed_host(ph): return invalid('Package host is not explicitly allowlisted')
   target='/tmp/security-package-'+str(item['update_id'])
   subprocess.run(['curl','--fail','--location','--silent','--show-error','--max-time','60',str(item['package_url']),'-o',target],check=True)
   digest=hashlib.sha256(Path(target).read_bytes()).hexdigest()
   if digest.lower()!=str(item['package_sha256']).lower(): return invalid('Package SHA-256 mismatch for '+str(item['update_id']))
  print('Signed catalog and package hashes verified before catalog processing.')
  return 0
 except Exception as exc:
  return invalid('Catalog validation failed: '+str(exc))
if __name__=='__main__': sys.exit(main())

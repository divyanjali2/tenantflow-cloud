import { Layers2 } from 'lucide-react'

export function Brand({ inverse = false }: { inverse?: boolean }) {
  return <div className={`brand ${inverse ? 'brand-inverse' : ''}`}>
    <span className="brand-mark"><Layers2 size={23} strokeWidth={2.1} aria-hidden="true" /></span>
    <span>TenantFlow<span className="brand-cloud">Cloud</span></span>
  </div>
}

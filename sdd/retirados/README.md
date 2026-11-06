# SDDs de módulos retirados

Estos documentos describen módulos que **ya no forman parte de Mesto Beach**: se retiraron al
separar el producto de Mesto (**ADR-023**), porque son maquinaria de un SaaS multi-tenant y aquí
hay un solo local que además factura con su propio TPV (**ADR-028**).

| SDD | Módulo | Por qué salió |
|---|---|---|
| `SDD-008` | Loyalty | Red de puntos entre restaurantes. Sin sentido con un único local; los puntos por cuenta de cliente siguen en Identity |
| `SDD-009` | Platform | Consola de Ops: alta de tenants, trials, CRM, facturación SaaS |
| `SDD-010` | Ledger | Contabilidad de doble partida del negocio de plataforma |

Se conservan como referencia por si el vertical "beach club" se vende a otro local y hay que
recuperar parte de esta maquinaria. **No describen el sistema actual.**

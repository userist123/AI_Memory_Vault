import { Column, Entity, Index, PrimaryColumn } from 'typeorm';

@Entity('auth_oidc_session_transactions')
export class OidcTransactionEntity {
  @PrimaryColumn({ type: 'varchar', length: 43 })
  transactionIdHash!: string;

  @Index()
  @Column({ type: 'varchar', length: 43 })
  stateHash!: string;

  @Column({ type: 'varchar', length: 43 })
  nonce!: string;

  @Index()
  @Column({ type: 'bigint' })
  expiresAt!: number;

  @Column({ type: 'varchar', length: 43, nullable: true })
  replacedSessionIdHash!: string | null;

  @Column({ type: 'varchar', length: 64 })
  pkceKeyId!: string;

  @Column({ type: 'varchar', length: 64 })
  pkceNonce!: string;

  @Column({ type: 'varchar', length: 64 })
  pkceTag!: string;

  @Column({ type: 'varchar', length: 256 })
  pkceCiphertext!: string;
}

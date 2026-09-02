import { Column, Entity, Index, PrimaryColumn } from 'typeorm';

@Entity('auth_oidc_session_sessions')
export class OidcSessionEntity {
  @PrimaryColumn({ type: 'varchar', length: 43 })
  sessionIdHash!: string;

  @Column({ type: 'varchar', length: 255 })
  subjectId!: string;

  @Column({ type: 'text' })
  rolesJson!: string;

  @Column({ type: 'text' })
  permissionsJson!: string;

  @Column({ type: 'bigint' })
  createdAt!: number;

  @Column({ type: 'bigint' })
  lastSeenAt!: number;

  @Index()
  @Column({ type: 'bigint' })
  absoluteExpiresAt!: number;

  @Column({ type: 'bigint' })
  accessExpiresAt!: number;

  @Column({ type: 'int', default: 1 })
  version!: number;

  @Column({ type: 'varchar', length: 43, nullable: true })
  refreshLeaseId!: string | null;

  @Column({ type: 'bigint', nullable: true })
  refreshLeaseExpiresAt!: number | null;

  @Column({ type: 'varchar', length: 64 })
  refreshKeyId!: string;

  @Column({ type: 'varchar', length: 64 })
  refreshNonce!: string;

  @Column({ type: 'varchar', length: 64 })
  refreshTag!: string;

  @Column({ type: 'text' })
  refreshCiphertext!: string;
}

import { Column, Entity, Index, PrimaryColumn } from 'typeorm';

@Entity('auth_local_session_sessions')
export class LocalAuthSessionEntity {
  @PrimaryColumn({ type: 'varchar', length: 43 })
  sessionTokenHash!: string;

  @Index()
  @Column({ type: 'varchar', length: 36 })
  accountId!: string;

  @Column({ type: 'varchar', length: 43 })
  csrfTokenHash!: string;

  @Column({ type: 'bigint' })
  createdAt!: number;

  @Column({ type: 'bigint' })
  lastSeenAt!: number;

  @Index()
  @Column({ type: 'bigint' })
  idleExpiresAt!: number;

  @Index()
  @Column({ type: 'bigint' })
  absoluteExpiresAt!: number;
}

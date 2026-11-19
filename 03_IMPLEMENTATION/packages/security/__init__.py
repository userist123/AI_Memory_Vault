"""Security boundary package.

Single regular package holding every security-boundary component (trust gate,
verified reduction, tool authorization, authorizer, mutation gate, ...). It
used to be split between a repository-level ``security`` directory and this
implementation package; both now live here so ``import security`` resolves to
exactly one location from a checkout and from an installed wheel.
"""

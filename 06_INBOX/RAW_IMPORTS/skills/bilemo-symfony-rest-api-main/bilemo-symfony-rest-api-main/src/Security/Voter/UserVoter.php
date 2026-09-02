<?php

// src/Security/Voter/UserVoter.php

namespace App\Security\Voter;

use App\Entity\User;
use App\Entity\Client;
use Symfony\Component\Security\Core\Authentication\Token\TokenInterface;
use Symfony\Component\Security\Core\Authorization\Voter\Voter;

final class UserVoter extends Voter
{
    public const VIEW = 'CAN_SEE_USER';
    public const EDIT = 'CAN_EDIT_USER';
    public const DELETE = 'CAN_DELETE_USER';

    protected function supports(string $attribute, mixed $subject): bool
    {
        // The voter is triggered only if the attribute matches one of our constants
        // and if the subject is an instance of the User entity
        return in_array($attribute, [self::VIEW, self::EDIT, self::DELETE], true)
            && $subject instanceof User;
    }

    /**
     * @param User $subject
     */
    protected function voteOnAttribute(string $attribute, mixed $subject, TokenInterface $token): bool
    {
        // 1. Retrieve the currently authenticated B2B Client via the JWT token
        $currentClient = $token->getUser();

        // If the client is not logged in or is not an instance of Client, deny access
        if (!$currentClient instanceof Client) {
            return false;
        }

        // 2. Perform security check based on the attribute requested
        // For BileMo, a Client can only view, edit, or delete a User if they own it
        switch ($attribute) {
            case self::VIEW:
            case self::EDIT:
            case self::DELETE:
                return $subject->getClient() === $currentClient;
        }

        return false;
    }
}
